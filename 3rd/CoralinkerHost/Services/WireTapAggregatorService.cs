using System.Collections.Concurrent;
using CoralinkerSDK;
using MCUSerialBridgeCLR;

namespace CoralinkerHost.Services;

/// <summary>
/// WireTap 数据聚合服务
/// - CAN 帧按 (UUID, PortIndex, Direction, RTR, DLC, CAN_ID) 聚合，推送快照
/// - Serial 帧节流批量推送
/// - 节点日志 (Console.WriteLine) 节流批量推送
/// - 周期 ~250ms 推送一次
/// </summary>
public sealed class WireTapAggregatorService : BackgroundService
{
    private readonly TerminalBroadcaster _terminal;
    private const int PushIntervalMs = 250;
    private const int MaxRecentFrames = 5;
    private const int FrameRateWindowMs = 1000;

    // CAN aggregation
    private readonly ConcurrentDictionary<CANAggregationKey, CANAggregatedGroup> _canGroups = new();

    // Serial batching
    private readonly ConcurrentQueue<SerialFrameItem> _serialQueue = new();

    // Node log batching
    private readonly ConcurrentQueue<(string UUID, string HostTimestamp, string Message, uint McuTimestampMs)> _nodeLogQueue = new();
    private volatile bool _nodeLogDirty;

    // dirty flags to avoid empty pushes
    private volatile bool _canDirty;
    private volatile bool _serialDirty;

    /// <summary>
    /// 清空所有聚合数据（Start 时调用）
    /// </summary>
    public void Reset()
    {
        _canGroups.Clear();
        while (_serialQueue.TryDequeue(out _)) { }
        while (_nodeLogQueue.TryDequeue(out _)) { }
        _canDirty = false;
        _serialDirty = false;
        _nodeLogDirty = false;
    }

    public WireTapAggregatorService(TerminalBroadcaster terminal)
    {
        _terminal = terminal;

        DIVERSession.Instance.OnWireTapData += HandleWireTapData;
        DIVERSession.Instance.OnNodeLog += HandleNodeLog;
    }

    /// <summary>
    /// 缓存节点日志，等待批量推送
    /// </summary>
    private void HandleNodeLog(string uuid, string hostTimestamp, string message, uint mcuTimestampMs)
    {
        _nodeLogQueue.Enqueue((uuid, hostTimestamp, message, mcuTimestampMs));
        _nodeLogDirty = true;
    }

    private void HandleWireTapData(WireTapDataEventArgs args)
    {
        if (args.PortType == PortType.CAN && args.CANMessage != null)
        {
            HandleCANFrame(args);
        }
        else
        {
            HandleSerialFrame(args);
        }
    }

    private void HandleCANFrame(WireTapDataEventArgs args)
    {
        var can = args.CANMessage!;
        var key = new CANAggregationKey(args.UUID, args.PortIndex, args.Direction, can.RTR, can.DLC, can.ID);

        var group = _canGroups.GetOrAdd(key, _ => new CANAggregatedGroup { Key = key, NodeName = args.NodeName });
        lock (group)
        {
            group.NodeName = args.NodeName;
            group.LastReceived = args.Timestamp;
            group.TotalFrameCount++;

            group.RecentFrames.Enqueue((can.Payload?.ToArray() ?? Array.Empty<byte>(), args.Timestamp, args.McuTimestampMs));
            while (group.RecentFrames.Count > MaxRecentFrames)
                group.RecentFrames.Dequeue();

            group.WindowTimestamps.AddLast(args.Timestamp);
        }

        _canDirty = true;
    }

    private void HandleSerialFrame(WireTapDataEventArgs args)
    {
        _serialQueue.Enqueue(new SerialFrameItem(
            args.UUID,
            args.NodeName,
            args.PortIndex,
            args.Direction,
            args.PortType.ToString(),
            args.RawData,
            args.Timestamp,
            args.McuTimestampMs
        ));
        _serialDirty = true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(PushIntervalMs, stoppingToken);

            try
            {
                if (_canDirty)
                {
                    _canDirty = false;
                    var snapshot = BuildCANSnapshot();
                    if (snapshot != null)
                        await _terminal.WireTapCanAggregatedAsync(snapshot, stoppingToken);
                }

                if (_serialDirty)
                {
                    _serialDirty = false;
                    var batch = DrainSerialQueue();
                    if (batch != null)
                        await _terminal.WireTapSerialBatchAsync(batch, stoppingToken);
                }

                if (_nodeLogDirty)
                {
                    _nodeLogDirty = false;
                    await DrainNodeLogQueue(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // ignore push errors
            }
        }
    }

    private object? BuildCANSnapshot()
    {
        var now = DateTime.Now;
        var groups = new List<object>();

        foreach (var kv in _canGroups)
        {
            var g = kv.Value;
            var key = kv.Key;

            lock (g)
            {
                // prune old timestamps from the sliding window
                while (g.WindowTimestamps.Count > 0 &&
                       (now - g.WindowTimestamps.First!.Value).TotalMilliseconds > FrameRateWindowMs)
                {
                    g.WindowTimestamps.RemoveFirst();
                }

                var recentFrames = g.RecentFrames.Select(f => new
                {
                    data = f.Payload,
                    timestamp = f.Timestamp.ToString("HH:mm:ss.fff"),
                    mcuTimestampMs = f.McuTimestampMs
                }).ToArray();

                groups.Add(new
                {
                    uuid = key.UUID,
                    nodeName = g.NodeName,
                    portIndex = (int)key.PortIndex,
                    direction = (int)key.Direction,
                    canId = (int)key.CANID,
                    dlc = key.DLC,
                    rtr = key.RTR,
                    lastReceived = g.LastReceived.ToString("O"),
                    frameRate = Math.Round(g.WindowTimestamps.Count / (FrameRateWindowMs / 1000.0), 1),
                    totalFrames = g.TotalFrameCount,
                    recentFrames
                });
            }
        }

        return groups.Count > 0 ? new { groups } : null;
    }

    private async Task DrainNodeLogQueue(CancellationToken ct)
    {
        var batches = new Dictionary<string, List<object>>();
        while (_nodeLogQueue.TryDequeue(out var item))
        {
            if (!batches.TryGetValue(item.UUID, out var list))
            {
                list = new List<object>();
                batches[item.UUID] = list;
            }
            list.Add(new { hostTimestamp = item.HostTimestamp, message = item.Message, mcuTimestampMs = item.McuTimestampMs });
        }

        foreach (var kv in batches)
        {
            await _terminal.NodeLogBatchAsync(kv.Key, kv.Value, ct);
        }
    }

    private object? DrainSerialQueue()
    {
        var entries = new List<object>();

        while (_serialQueue.TryDequeue(out var item))
        {
            entries.Add(new
            {
                uuid = item.UUID,
                nodeName = item.NodeName,
                portIndex = (int)item.PortIndex,
                direction = (int)item.Direction,
                portType = item.PortType,
                rawData = item.RawData,
                timestamp = item.Timestamp.ToString("O"),
                mcuTimestampMs = item.McuTimestampMs
            });
        }

        return entries.Count > 0 ? new { entries } : null;
    }
}

// Internal models

public record CANAggregationKey(string UUID, byte PortIndex, byte Direction, bool RTR, int DLC, ushort CANID);

public class CANAggregatedGroup
{
    public CANAggregationKey Key = null!;
    public string NodeName = "";
    public Queue<(byte[] Payload, DateTime Timestamp, uint McuTimestampMs)> RecentFrames = new();
    public LinkedList<DateTime> WindowTimestamps = new();
    public long TotalFrameCount;
    public DateTime LastReceived;
}

public record SerialFrameItem(
    string UUID,
    string NodeName,
    byte PortIndex,
    byte Direction,
    string PortType,
    byte[] RawData,
    DateTime Timestamp,
    uint McuTimestampMs
);
