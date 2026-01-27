using System.Globalization;
using CoralinkerSDK;

namespace CoralinkerHost.Services;

/// <summary>
/// 变量和节点状态推送服务
/// 每 200ms 推送变量快照，每 1000ms 推送节点状态
/// </summary>
public sealed class VariableInspectorPushService : BackgroundService
{
    private readonly TerminalBroadcaster _terminal;

    public VariableInspectorPushService(TerminalBroadcaster terminal)
    {
        _terminal = terminal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastNodePush = DateTime.UtcNow;
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var session = DIVERSession.Instance;
            
            // 每 200ms 推送变量快照
            try
            {
                var varsSnapshot = BuildVarsSnapshot(session);
                if (varsSnapshot != null)
                {
                    await _terminal.VarsSnapshotAsync(varsSnapshot, stoppingToken);
                }
            }
            catch
            {
                // ignore polling errors
            }

            // 每 1000ms 推送节点状态
            if ((DateTime.UtcNow - lastNodePush).TotalMilliseconds >= 1000)
            {
                try
                {
                    var nodeSnapshot = BuildNodeSnapshot(session);
                    if (nodeSnapshot != null)
                    {
                        await _terminal.NodeSnapshotAsync(nodeSnapshot, stoppingToken);
                    }
                }
                catch
                {
                    // ignore polling errors
                }

                lastNodePush = DateTime.UtcNow;
            }

            await Task.Delay(200, stoppingToken);
        }
    }

    private static object? BuildVarsSnapshot(DIVERSession session)
    {
        var fields = session.GetAllCartFields();
        if (fields.Count == 0) return null;

        var items = new List<object>();
        
        foreach (var kv in fields)
        {
            var field = kv.Value;
            var (dir, icon) = (field.IsLowerIO, field.IsUpperIO, field.IsMutual) switch
            {
                (true, _, _) => ("lower", "arrow-down"),
                (_, true, _) => ("upper", "arrow-up"),
                (_, _, true) => ("mutual", "arrow-up"),
                _ => ("none", "circle")
            };

            items.Add(new
            {
                name = field.Name,
                type = field.Type,
                direction = dir,
                icon,
                value = FormatValue(field.Value)
            });
        }

        return new
        {
            targetType = "DIVERSession",
            fields = items
        };
    }

    private static object? BuildNodeSnapshot(DIVERSession session)
    {
        var states = session.GetNodeStates();
        if (states.Count == 0) return null;

        var nodes = states.Values.Select(state =>
        {
            var info = session.GetNodeInfo(state.UUID);
            
            return new
            {
                nodeId = state.UUID,
                nodeName = state.NodeName,
                mcuUri = state.McuUri,
                isConnected = state.IsConnected,
                runState = state.RunState,
                isConfigured = state.IsConfigured,
                isProgrammed = state.IsProgrammed,
                hasProgram = info?.HasProgram ?? false,
                logicName = info?.LogicName,
                version = info?.Version == null ? null : new
                {
                    productionName = info.Version.ProductionName,
                    gitTag = info.Version.GitTag,
                    gitCommit = info.Version.GitCommit,
                    buildTime = info.Version.BuildTime
                },
                layout = info?.Layout == null ? null : new
                {
                    digitalInputCount = info.Layout.DigitalInputCount,
                    digitalOutputCount = info.Layout.DigitalOutputCount,
                    portCount = info.Layout.PortCount
                },
                ports = info?.PortConfigs.Select((p, i) => new
                {
                    index = i,
                    type = p.Type,
                    baud = p.Baud,
                    receiveFrameMs = p.ReceiveFrameMs,
                    retryTimeMs = p.RetryTimeMs
                }).ToArray(),
                stats = state.Stats == null ? null : new
                {
                    uptimeMs = state.Stats.UptimeMs,
                    digitalInputs = state.Stats.DigitalInputs,
                    digitalOutputs = state.Stats.DigitalOutputs,
                    ports = state.Stats.Ports.Select(p => new
                    {
                        index = p.Index,
                        txFrames = p.TxFrames,
                        rxFrames = p.RxFrames,
                        txBytes = p.TxBytes,
                        rxBytes = p.RxBytes
                    }).ToArray()
                }
            };
        }).ToArray();

        return new { nodes };
    }

    private static string FormatValue(object? v)
    {
        if (v == null) return "null";
        if (v is string s) return s;
        if (v is Array a)
        {
            var parts = new List<string>();
            foreach (var e in a) parts.Add(FormatValue(e));
            return "[" + string.Join(", ", parts) + "]";
        }
        if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
        return v.ToString() ?? "";
    }
}
