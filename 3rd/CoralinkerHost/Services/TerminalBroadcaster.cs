using CoralinkerHost.Web;
using Microsoft.AspNetCore.SignalR;

namespace CoralinkerHost.Services;

public sealed class TerminalBroadcaster
{
    private readonly IHubContext<TerminalHub> _hub;
    
    // Terminal 日志缓冲区
    private readonly List<string> _terminalBuffer = new();
    private readonly object _terminalBufferLock = new();
    
    // Build 日志缓冲区
    private readonly List<string> _buildBuffer = new();
    private readonly object _buildBufferLock = new();
    
    private const int MAX_BUFFER_SIZE = 5000;

    public TerminalBroadcaster(IHubContext<TerminalHub> hub)
    {
        _hub = hub;
    }

    /// <summary>
    /// 发送 Terminal 日志行
    /// </summary>
    public Task LineAsync(string line, CancellationToken ct = default)
    {
        var timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss.fff");
        var formattedLine = $"[SA][{timestamp}] {line}";
        
        // 存储到缓冲区
        lock (_terminalBufferLock)
        {
            _terminalBuffer.Add(formattedLine);
            if (_terminalBuffer.Count > MAX_BUFFER_SIZE)
            {
                _terminalBuffer.RemoveRange(0, _terminalBuffer.Count - MAX_BUFFER_SIZE);
            }
        }
        
        return _hub.Clients.All.SendAsync("terminalLine", formattedLine, ct);
    }
    
    /// <summary>
    /// 发送 Build 日志行（专用通道，发送到前端 Build 面板）
    /// </summary>
    public Task BuildLineAsync(string line, CancellationToken ct = default)
    {
        var timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss.fff");
        var formattedLine = $"[SA][{timestamp}] {line}";
        
        // 存储到 Build 缓冲区
        lock (_buildBufferLock)
        {
            _buildBuffer.Add(formattedLine);
            if (_buildBuffer.Count > MAX_BUFFER_SIZE)
            {
                _buildBuffer.RemoveRange(0, _buildBuffer.Count - MAX_BUFFER_SIZE);
            }
        }
        
        return _hub.Clients.All.SendAsync("buildLine", formattedLine, ct);
    }
    
    /// <summary>
    /// 发送 Build 日志行（原始格式，不加时间戳）
    /// </summary>
    public Task BuildLineRawAsync(string line, CancellationToken ct = default)
    {
        // 存储到 Build 缓冲区
        lock (_buildBufferLock)
        {
            _buildBuffer.Add(line);
            if (_buildBuffer.Count > MAX_BUFFER_SIZE)
            {
                _buildBuffer.RemoveRange(0, _buildBuffer.Count - MAX_BUFFER_SIZE);
            }
        }
        
        return _hub.Clients.All.SendAsync("buildLine", line, ct);
    }
    
    /// <summary>
    /// 清空 Build 日志缓冲区
    /// </summary>
    public void ClearBuildHistory()
    {
        lock (_buildBufferLock)
        {
            _buildBuffer.Clear();
        }
    }
    
    /// <summary>
    /// 获取 Terminal 历史日志
    /// </summary>
    public string[] GetHistory()
    {
        lock (_terminalBufferLock)
        {
            return _terminalBuffer.ToArray();
        }
    }
    
    /// <summary>
    /// 清空 Terminal 日志
    /// </summary>
    public void ClearHistory()
    {
        lock (_terminalBufferLock)
        {
            _terminalBuffer.Clear();
        }
    }
    
    /// <summary>
    /// 获取 Build 历史日志
    /// </summary>
    public string[] GetBuildHistory()
    {
        lock (_buildBufferLock)
        {
            return _buildBuffer.ToArray();
        }
    }

    public Task VarsSnapshotAsync(object snapshot, CancellationToken ct = default)
    {
        return _hub.Clients.All.SendAsync("varsSnapshot", snapshot, ct);
    }

    public Task NodeSnapshotAsync(object snapshot, CancellationToken ct = default)
    {
        return _hub.Clients.All.SendAsync("nodeSnapshot", snapshot, ct);
    }
    
    public Task NodeLogLineAsync(string uuid, string message, CancellationToken ct = default)
    {
        // 发送两个参数：uuid 和 message（message 已经带时间戳）
        return _hub.Clients.All.SendAsync("nodeLogLine", uuid, message, ct);
    }

    /// <summary>
    /// 推送固件升级进度
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <param name="progress">进度百分比（0-100）</param>
    /// <param name="stage">当前阶段</param>
    /// <param name="message">可选消息</param>
    /// <param name="ct">取消令牌</param>
    public Task UpgradeProgressAsync(string nodeId, int progress, string stage, string? message, CancellationToken ct = default)
    {
        return _hub.Clients.All.SendAsync("upgradeProgress", nodeId, progress, stage, message, ct);
    }

    /// <summary>
    /// 推送 MCU 致命错误（HardFault 或 ASSERT 失败）- 接收已格式化的 JSON 字符串
    /// </summary>
    public Task FatalErrorJsonAsync(string errorJson, CancellationToken ct = default)
    {
        // 解析 JSON 字符串为对象后发送，这样 SignalR 会正确序列化
        var errorData = System.Text.Json.JsonSerializer.Deserialize<object>(errorJson);
        return _hub.Clients.All.SendAsync("fatalError", errorData, ct);
    }
}
