using CoralinkerSDK;

namespace CoralinkerHost.Services;

/// <summary>
/// 运行时会话服务 - DIVERSession 的简单封装，只添加日志广播
/// </summary>
public sealed class RuntimeSessionService
{
    private readonly TerminalBroadcaster _terminal;
    private readonly DIVERSession _session = DIVERSession.Instance;

    public RuntimeSessionService(TerminalBroadcaster terminal)
    {
        _terminal = terminal;
        
        // 订阅节点日志事件，广播到 SignalR
        _session.OnNodeLog += async (uuid, message) =>
        {
            try
            {
                await _terminal.NodeLogLineAsync(uuid, message);
            }
            catch
            {
                // ignore broadcast errors
            }
        };

        // 订阅致命错误事件，广播到 SignalR（JSON 已由 DIVERSession 格式化）
        _session.OnFatalError += async (uuid, errorJson) =>
        {
            try
            {
                await _terminal.FatalErrorJsonAsync(errorJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RuntimeSessionService] Error broadcasting fatal error: {ex.Message}");
            }
        };
    }

    /// <summary>
    /// 启动会话
    /// </summary>
    public async Task<StartResult> StartAsync(CancellationToken ct)
    {
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
    public async Task<string?> AddNodeAsync(string mcuUri, CancellationToken ct)
    {
        await _terminal.LineAsync($"[session] Adding node at {mcuUri}...", ct);
        var uuid = _session.AddNode(mcuUri);
        
        if (uuid != null)
        {
            var info = _session.GetNodeInfo(uuid);
            await _terminal.LineAsync($"[session] ✓ Added {info?.NodeName ?? uuid}", ct);
        }
        else
        {
            await _terminal.LineAsync($"[session] ✗ Add node failed", ct);
        }
        
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
    public async Task<bool> ProgramNodeAsync(string uuid, byte[] programBytes, string metaJson, string? logicName, CancellationToken ct)
    {
        var info = _session.GetNodeInfo(uuid);
        var result = _session.ProgramNode(uuid, programBytes, metaJson, logicName);
        
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
