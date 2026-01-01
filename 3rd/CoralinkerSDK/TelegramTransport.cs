namespace CoralinkerSDK;

/// <summary>
/// Placeholder abstraction for a "serial telegram" transport layer (not yet integrated).
/// </summary>
public interface ICoralinkerTelegramTransport
{
    void Send(ReadOnlySpan<byte> telegram);
    int Receive(Span<byte> buffer);
}

/// <summary>
/// Dummy implementation used to mark unimplemented telegram paths.
/// </summary>
public sealed class ThrowingTelegramTransport : ICoralinkerTelegramTransport
{
    public void Send(ReadOnlySpan<byte> telegram)
    {
        throw new NotImplementedException("Coralinker telegram transport is Pending.");
    }

    public int Receive(Span<byte> buffer)
    {
        throw new NotImplementedException("Coralinker telegram transport is Pending.");
    }
}


