using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoralinkerHost.Services;

public sealed class FatalErrorStore
{
    private readonly object _gate = new();
    private FatalErrorSnapshot? _latest;

    public void Record(string uuid, string errorJson)
    {
        JsonNode? data = null;
        try
        {
            data = JsonNode.Parse(errorJson);
        }
        catch
        {
            data = JsonSerializer.SerializeToNode(new { raw = errorJson });
        }

        lock (_gate)
        {
            _latest = new FatalErrorSnapshot(
                Seq: (_latest?.Seq ?? 0) + 1,
                Uuid: uuid,
                Timestamp: DateTimeOffset.Now,
                Data: data
            );
        }
    }

    public FatalErrorSnapshot? GetLatest()
    {
        lock (_gate)
        {
            return _latest;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _latest = null;
        }
    }
}

public sealed record FatalErrorSnapshot(long Seq, string Uuid, DateTimeOffset Timestamp, JsonNode? Data);
