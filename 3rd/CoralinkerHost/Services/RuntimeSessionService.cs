using CoralinkerSDK;
using System.Text.Json.Nodes;

namespace CoralinkerHost.Services;

/// <summary>
/// 运行时会话服务 - DIVERSession 的简单封装，只添加日志广播
/// </summary>
public sealed class RuntimeSessionService
{
    private readonly TerminalBroadcaster _terminal;
    private readonly WireTapAggregatorService _aggregator;
    private readonly RootRuntimeService _rootRuntime;
    private readonly FatalErrorStore _fatalErrors;
    private readonly DIVERSession _session = DIVERSession.Instance;

    public RuntimeSessionService(TerminalBroadcaster terminal, WireTapAggregatorService aggregator, RootRuntimeService rootRuntime, FatalErrorStore fatalErrors)
    {
        _terminal = terminal;
        _aggregator = aggregator;
        _rootRuntime = rootRuntime;
        _fatalErrors = fatalErrors;
        
        // 节点日志事件现在由 WireTapAggregatorService 批量推送（nodeLogBatch）
        // 减少高频 Console.WriteLine 导致的 SignalR 推送风暴

        // 订阅致命错误事件，广播到 SignalR（JSON 已由 DIVERSession 格式化）
        _session.OnFatalError += async (uuid, errorJson) =>
        {
            try
            {
                _fatalErrors.Record(uuid, errorJson);
                await _terminal.FatalErrorJsonAsync(errorJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RuntimeSessionService] Error broadcasting fatal error: {ex.Message}");
            }
        };

        // WireTap 数据事件现在由 WireTapAggregatorService 处理：
        // - CAN 帧聚合后通过 wireTapCanAggregated 推送
        // - Serial 帧节流后通过 wireTapSerialBatch 批量推送
    }

    /// <summary>
    /// 启动会话
    /// </summary>
    public async Task<StartResult> StartAsync(CancellationToken ct)
    {
        _aggregator.Reset();

        await _terminal.LineAsync("[session] ========== Starting ==========", ct);
        
        var result = _session.Start();
        
        if (result.Success)
        {
            await _terminal.LineAsync($"[session] Started {result.SuccessNodes}/{result.TotalNodes} node(s)", ct);
        }
        else
        {
            await _terminal.LineAsync($"[session] Start failed: {result.SuccessNodes}/{result.TotalNodes} succeeded", ct);
        }
        
        foreach (var error in result.Errors)
        {
            await _terminal.LineAsync($"[session] ✗ {error.NodeName}: {error.Error}", ct);
        }
        
        if (result.Success)
        {
            await _rootRuntime.StartAsync(ct);
            await _terminal.LineAsync("[session] ========== Running ==========", ct);
        }
        
        return result;
    }

    /// <summary>
    /// 停止会话
    /// </summary>
    public async Task StopAsync(CancellationToken ct)
    {
        await _terminal.LineAsync("[session] ========== Stopping ==========", ct);
        await _rootRuntime.StopAsync(ct);
        _session.Stop();
        await _terminal.LineAsync("[session] ========== Stopped ==========", ct);
    }

    /// <summary>
    /// 探测节点
    /// </summary>
    public async Task<NodeProbeResult?> ProbeNodeAsync(string mcuUri, CancellationToken ct)
    {
        await _terminal.LineAsync($"[session] Probing {mcuUri}...", ct);
        var result = _session.ProbeNode(mcuUri);
        
        if (result != null)
        {
            await _terminal.LineAsync($"[session] ✓ Probe OK: {result.Version.ProductionName ?? "Unknown"}", ct);
        }
        else
        {
            await _terminal.LineAsync($"[session] ✗ Probe failed", ct);
        }
        
        return result;
    }

    /// <summary>
    /// 添加节点
    /// </summary>
    public async Task<AddNodeOutcome> AddNodeAsync(string mcuUri, CancellationToken ct)
    {
        await _terminal.LineAsync($"[session] Adding node at {mcuUri}...", ct);
        var outcome = _session.AddNode(mcuUri);

        if (outcome.Success)
        {
            var info = _session.GetNodeInfo(outcome.Uuid!);
            await _terminal.LineAsync($"[session] ✓ Added {info?.NodeName ?? outcome.Uuid}", ct);
        }
        else
        {
            await _terminal.LineAsync($"[session] ✗ Add node failed: {outcome.Message}", ct);
        }

        return outcome;
    }

    /// <summary>清除节点已编程逻辑，回到空状态并释放占用的 Logic。</summary>
    public async Task<bool> UnprogramNodeAsync(string uuid, CancellationToken ct)
    {
        var ok = _session.UnprogramNode(uuid);
        await _terminal.LineAsync(
            ok ? $"[session] Cleared logic on {uuid}" : $"[session] ✗ Clear logic failed: {uuid}",
            ct
        );
        return ok;
    }

    /// <summary>
    /// 添加独立进程隔离的模拟节点。
    /// </summary>
    public async Task<string> AddSimulatedNodeAsync(string? name, CancellationToken ct)
    {
        await _terminal.LineAsync("[session] Adding simulated node...", ct);
        var uuid = _session.AddSimulatedNode(name);
        var info = _session.GetNodeInfo(uuid);
        await _terminal.LineAsync($"[session] ✓ Added simulated node {info?.NodeName ?? uuid}", ct);
        return uuid;
    }

    /// <summary>
    /// 删除节点
    /// </summary>
    public async Task<bool> RemoveNodeAsync(string uuid, CancellationToken ct)
    {
        var info = _session.GetNodeInfo(uuid);
        var result = _session.RemoveNode(uuid);
        
        if (result)
        {
            await _terminal.LineAsync($"[session] ✓ Removed {info?.NodeName ?? uuid}", ct);
        }
        
        return result;
    }

    /// <summary>
    /// 设置节点代码
    /// </summary>
    public async Task<bool> ProgramNodeAsync(string uuid, byte[] programBytes, string metaJson, string? logicName, JsonObject? buildInfo, CancellationToken ct)
    {
        var info = _session.GetNodeInfo(uuid);
        var result = _session.ProgramNode(uuid, programBytes, metaJson, logicName, buildInfo);
        
        if (result)
        {
            await _terminal.LineAsync($"[session] ✓ Programmed {info?.NodeName ?? uuid}: {programBytes.Length} bytes, logic={logicName ?? "(none)"}", ct);
        }
        else
        {
            await _terminal.LineAsync($"[session] ✗ Program failed for {info?.NodeName ?? uuid}", ct);
        }
        
        return result;
    }
}
