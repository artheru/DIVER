using MCUSerialBridgeCLR;

namespace CoralinkerSDK;

/// <summary>
/// 单个 MCU 节点，封装 MCUSerialBridge
/// </summary>
public class MCUNode : IDisposable
{
    private const uint DefaultBaudRate = 2000000;
    private const uint DefaultTimeout = 500;
    private const uint ProgramTimeout = 10000;

    private MCUSerialBridge? _bridge;
    private bool _disposed;

    /// <summary>节点唯一标识</summary>
    public string NodeId { get; }
    
    /// <summary>MCU 串口 URI</summary>
    public string McuUri { get; }
    
    /// <summary>是否已连接</summary>
    public bool IsConnected => _bridge?.IsOpen ?? false;
    
    /// <summary>是否正在运行</summary>
    public bool IsRunning => State?.IsRunning ?? false;
    
    /// <summary>MCU 版本信息</summary>
    public VersionInfo? Version { get; private set; }
    
    /// <summary>MCU 状态</summary>
    public MCUState? State { get; private set; }
    
    /// <summary>最后一次错误信息</summary>
    public string? LastError { get; private set; }
    
    /// <summary>DIVER 程序字节码</summary>
    public byte[] ProgramBytes { get; set; } = Array.Empty<byte>();
    
    /// <summary>端口配置</summary>
    public PortConfig[] PortConfigs { get; set; } = Array.Empty<PortConfig>();
    
    /// <summary>Cart 字段元数据（从 MetaJson 解析）</summary>
    public CartFieldInfo[] CartFields { get; set; } = Array.Empty<CartFieldInfo>();
    
    /// <summary>LowerIO 数据接收事件（由 DIVERSession 订阅）</summary>
    internal event Action<byte[]>? OnLowerIOReceived;
    
    /// <summary>控制台输出事件</summary>
    internal event Action<string>? OnConsoleOutput;

    public MCUNode(string nodeId, string mcuUri)
    {
        NodeId = nodeId;
        McuUri = mcuUri;
    }

    /// <summary>
    /// 连接到 MCU (Open + Reset + GetVersion)
    /// </summary>
    public bool Connect()
    {
        if (_bridge?.IsOpen == true)
        {
            LastError = "Already connected";
            return true;
        }

        try
        {
            var (portName, baudRate) = ParseUri(McuUri);
            
            _bridge = new MCUSerialBridge();
            var err = _bridge.Open(portName, baudRate);
            if (err != MCUSerialBridgeError.OK)
            {
                LastError = $"Open failed: {err.ToDescription()}";
                return false;
            }

            // Reset MCU
            err = _bridge.Reset(DefaultTimeout);
            if (err != MCUSerialBridgeError.OK)
            {
                LastError = $"Reset failed: {err.ToDescription()}";
                Disconnect();
                return false;
            }
            Thread.Sleep(300);

            // Get version to verify connection
            err = _bridge.GetVersion(out var version, DefaultTimeout);
            if (err != MCUSerialBridgeError.OK)
            {
                LastError = $"GetVersion failed: {err.ToDescription()}";
                Disconnect();
                return false;
            }
            Version = version;

            // Get state
            err = _bridge.GetState(out var state, DefaultTimeout);
            if (err == MCUSerialBridgeError.OK)
            {
                State = state;
            }

            // Register callbacks
            _bridge.RegisterMemoryLowerIOCallback(data => OnLowerIOReceived?.Invoke(data));
            _bridge.RegisterConsoleWriteLineCallback(msg => OnConsoleOutput?.Invoke(msg));

            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Connect exception: {ex.Message}";
            Disconnect();
            return false;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect()
    {
        if (_bridge != null)
        {
            try { _bridge.Dispose(); } catch { }
            _bridge = null;
        }
        State = null;
        Version = null;
    }

    /// <summary>
    /// 配置 MCU 端口（使用 PortConfigs 属性）
    /// </summary>
    public bool Configure()
    {
        if (_bridge == null || !_bridge.IsOpen)
        {
            LastError = "Not connected";
            return false;
        }

        if (PortConfigs.Length == 0)
        {
            // 无端口配置，跳过
            LastError = null;
            return true;
        }

        var err = _bridge.Configure(PortConfigs, DefaultTimeout);
        if (err != MCUSerialBridgeError.OK)
        {
            LastError = $"Configure failed: {err.ToDescription()}";
            return false;
        }

        RefreshState();
        LastError = null;
        return true;
    }

    /// <summary>
    /// 下载 DIVER 程序到 MCU（使用 ProgramBytes 属性）
    /// </summary>
    public bool Program()
    {
        if (_bridge == null || !_bridge.IsOpen)
        {
            LastError = "Not connected";
            return false;
        }

        if (ProgramBytes.Length == 0)
        {
            LastError = "No program bytes";
            return false;
        }

        var err = _bridge.Program(ProgramBytes, ProgramTimeout);
        if (err != MCUSerialBridgeError.OK)
        {
            LastError = $"Program failed: {err.ToDescription()}";
            return false;
        }

        RefreshState();
        LastError = null;
        return true;
    }

    /// <summary>
    /// 启动 MCU 执行
    /// </summary>
    public bool Start()
    {
        if (_bridge == null || !_bridge.IsOpen)
        {
            LastError = "Not connected";
            return false;
        }

        var err = _bridge.Start(DefaultTimeout);
        if (err != MCUSerialBridgeError.OK)
        {
            LastError = $"Start failed: {err.ToDescription()}";
            return false;
        }

        RefreshState();
        LastError = null;
        return true;
    }

    /// <summary>
    /// 停止 MCU 执行（通过 Reset）
    /// </summary>
    public bool Stop()
    {
        if (_bridge == null || !_bridge.IsOpen)
        {
            LastError = "Not connected";
            return false;
        }

        var err = _bridge.Reset(DefaultTimeout);
        if (err != MCUSerialBridgeError.OK)
        {
            LastError = $"Stop (Reset) failed: {err.ToDescription()}";
            return false;
        }

        RefreshState();
        LastError = null;
        return true;
    }

    /// <summary>
    /// 发送 UpperIO 数据到 MCU
    /// </summary>
    internal bool SendUpperIO(byte[] data, uint timeoutMs = 20)
    {
        if (_bridge == null || !_bridge.IsOpen)
        {
            LastError = "Not connected";
            return false;
        }

        var err = _bridge.MemoryUpperIO(data, timeoutMs);
        if (err != MCUSerialBridgeError.OK)
        {
            LastError = $"UpperIO failed: {err.ToDescription()}";
            return false;
        }

        LastError = null;
        return true;
    }

    /// <summary>
    /// 刷新 MCU 状态
    /// </summary>
    public void RefreshState()
    {
        if (_bridge == null || !_bridge.IsOpen) return;
        
        var err = _bridge.GetState(out var state, DefaultTimeout);
        if (err == MCUSerialBridgeError.OK)
        {
            State = state;
        }
    }

    private static (string portName, uint baudRate) ParseUri(string uri)
    {
        // 简单端口名称
        if (!uri.StartsWith("serial://", StringComparison.OrdinalIgnoreCase))
        {
            return (uri, DefaultBaudRate);
        }

        // URI 格式: serial://name=COM3&baudrate=2000000
        var paramString = uri.Substring("serial://".Length);
        var parameters = paramString.Split('&')
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        uint baudRate = DefaultBaudRate;
        if (parameters.TryGetValue("baudrate", out var baudStr) && uint.TryParse(baudStr, out var b))
        {
            baudRate = b;
        }

        if (parameters.TryGetValue("name", out var name))
        {
            return (name, baudRate);
        }

        // 如果指定了 VID/PID，使用 SerialPortResolver 解析
        if (parameters.TryGetValue("vid", out var vid) && parameters.TryGetValue("pid", out var pid))
        {
            var ports = SerialPortResolver.ResolveByVidPid(vid, pid);
            if (ports.Length > 0)
            {
                return (ports[0], baudRate);
            }
        }

        throw new ArgumentException($"Cannot parse URI: {uri}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}
