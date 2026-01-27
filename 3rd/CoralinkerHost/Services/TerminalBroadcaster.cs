using CoralinkerHost.Web;
using Microsoft.AspNetCore.SignalR;

namespace CoralinkerHost.Services;

public sealed class TerminalBroadcaster
{
    private readonly IHubContext<TerminalHub> _hub;

    public TerminalBroadcaster(IHubContext<TerminalHub> hub)
    {
        _hub = hub;
    }

    public Task LineAsync(string line, CancellationToken ct = default)
    {
        var timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss.fff");
        return _hub.Clients.All.SendAsync("terminalLine", $"[SA][{timestamp}] {line}", ct);
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
