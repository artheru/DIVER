using MCUSerialBridgeCLR;

namespace CoralinkerSDK;

public interface IRuntimeNode : IDisposable
{
    string NodeId { get; }
    string McuUri { get; }
    bool IsConnected { get; }
    bool IsRunning { get; }
    VersionInfo? Version { get; }
    LayoutInfo? Layout { get; }
    MCUState? State { get; }
    RuntimeStats? Stats { get; }
    string? LastError { get; }
    byte[] ProgramBytes { get; set; }
    PortConfig[] PortConfigs { get; set; }
    CartFieldInfo[] CartFields { get; set; }

    event Action<byte[]>? OnLowerIOReceived;
    event Action<string, uint>? OnConsoleOutput;
    event Action<ErrorPayload>? OnFatalError;
    event Action<string>? OnError;

    bool Connect();
    void Disconnect();
    bool Configure();
    bool Program();
    bool Start();
    bool Stop();
    bool SendUpperIO(byte[] data, uint timeoutMs = 20);
    bool SetWireTap(byte portIndex, WireTapFlags flags, uint timeoutMs = 200);
    bool RegisterSerialPortCallback(byte portIndex, Action<byte, byte, byte[], uint> callback);
    bool RegisterCANPortCallback(byte portIndex, Action<byte, byte, CANMessage, uint> callback);
    void RefreshState();
    void RefreshStats();
}
