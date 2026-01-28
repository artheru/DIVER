using CoralinkerHost.Web;
using Microsoft.AspNetCore.SignalR;

namespace CoralinkerHost.Services;

public sealed class TerminalBroadcaster
{
    private readonly IHubContext<TerminalHub> _hub;
    
    // Terminal 日志缓冲区
    private readonly List<string> _terminalBuffer = new();
    private readonly object _bufferLock = new();
    private const int MAX_BUFFER_SIZE = 5000;

    public TerminalBroadcaster(IHubContext<TerminalHub> hub)
    {
        _hub = hub;
    }

    public Task LineAsync(string line, CancellationToken ct = default)
    {
        var timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss.fff");
        var formattedLine = $"[SA][{timestamp}] {line}";
        
        // 存储到缓冲区
        lock (_bufferLock)
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
    /// 获取 Terminal 历史日志
    /// </summary>
    public string[] GetHistory()
    {
        lock (_bufferLock)
        {
            return _terminalBuffer.ToArray();
        }
    }
    
    /// <summary>
    /// 清空 Terminal 日志
    /// </summary>
    public void ClearHistory()
    {
        lock (_bufferLock)
        {
            _terminalBuffer.Clear();
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
}
