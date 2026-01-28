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

    public static readonly int ResetWaitTime = 1000;

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
    
    /// <summary>MCU 硬件布局信息</summary>
    public LayoutInfo? Layout { get; private set; }
    
    /// <summary>MCU 状态</summary>
    public MCUState? State { get; private set; }
    
    /// <summary>MCU 运行时统计数据（由 DIVERSession 定期刷新）</summary>
    public RuntimeStats? Stats { get; private set; }
    
    /// <summary>最后一次错误信息</summary>
    public string? LastError { get; private set; }
    
    /// <summary>DIVER 程序字节码</summary>
    public byte[] ProgramBytes { get; set; } = Array.Empty<byte>();
    
    /// <summary>端口配置（如果为空，Connect 后会根据 Layout 初始化）</summary>
    public PortConfig[] PortConfigs { get; set; } = Array.Empty<PortConfig>();
    
    /// <summary>PortConfigs 是否由用户显式设置（用于判断是否需要初始化）</summary>
    private bool _portConfigsExplicitlySet;
    
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
            Thread.Sleep(ResetWaitTime);

            // Get version to verify connection
            err = _bridge.GetVersion(out var version, DefaultTimeout);
            if (err != MCUSerialBridgeError.OK)
            {
                LastError = $"GetVersion failed: {err.ToDescription()}";
                Disconnect();
                return false;
            }
            Version = version;

            // Get hardware layout
            err = _bridge.GetLayout(out var layout, DefaultTimeout);
            if (err == MCUSerialBridgeError.OK)
            {
                Layout = layout;
                // Initialize or validate PortConfigs based on Layout
                InitializeOrValidatePortConfigs(layout);
            }
            else
            {
                // Layout not available (older firmware), continue without it
                Console.WriteLine($"[MCUNode] GetLayout failed: {err.ToDescription()} - using default config");
            }

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
        Stats = null;
        Version = null;
        Layout = null;
    }

    /// <summary>
    /// 根据 Layout 初始化或验证 PortConfigs
    /// </summary>
    private void InitializeOrValidatePortConfigs(LayoutInfo layout)
    {
        var validPorts = layout.GetValidPorts();
        if (validPorts.Length == 0) return;

        // 如果已有 PortConfigs，验证数量是否匹配
        if (PortConfigs.Length > 0)
        {
            if (PortConfigs.Length != validPorts.Length)
            {
                Console.WriteLine($"[MCUNode] Warning: PortConfigs count ({PortConfigs.Length}) doesn't match Layout ({validPorts.Length}), reinitializing");
                PortConfigs = Array.Empty<PortConfig>();
            }
            else
            {
                // 验证端口类型是否匹配
                for (int i = 0; i < validPorts.Length; i++)
                {
                    var expectedType = validPorts[i].Type;
                    var actualType = PortConfigs[i].PortType;
                    var expectedByte = expectedType == PortType.CAN ? (byte)0x02 : (byte)0x01;
                    if (actualType != expectedByte)
                    {
                        Console.WriteLine($"[MCUNode] Warning: Port[{i}] type mismatch (expected {expectedType}, got {actualType}), reinitializing");
                        PortConfigs = Array.Empty<PortConfig>();
                        break;
                    }
                }
            }
        }

        // 如果 PortConfigs 为空，根据 Layout 初始化
        if (PortConfigs.Length == 0)
        {
            var configs = new List<PortConfig>();
            foreach (var port in validPorts)
            {
                if (port.Type == PortType.CAN)
                {
                    configs.Add(new CANPortConfig(500000, 10));
                }
                else
                {
                    configs.Add(new SerialPortConfig(9600, 20));
                }
            }
            PortConfigs = configs.ToArray();
            Console.WriteLine($"[MCUNode] Initialized {configs.Count} port configs from Layout");
        }
    }

    /// <summary>
    /// 设置端口配置（显式设置）
    /// </summary>
    public void SetPortConfigs(PortConfig[] configs)
    {
        PortConfigs = configs;
        _portConfigsExplicitlySet = true;
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
        if (_bridge == null || !_bridge.IsOpen)
        {
            State = null; // Mark as offline
            return;
        }
        
        var err = _bridge.GetState(out var state, DefaultTimeout);
        if (err == MCUSerialBridgeError.OK)
        {
            State = state;
            LastError = null;
        }
        else
        {
            // Timeout or communication error - mark as offline
            State = null;
            LastError = $"GetState failed: {err.ToDescription()}";
        }
    }

    /// <summary>
    /// 刷新 MCU 运行时统计数据（由 DIVERSession 调用）
    /// </summary>
    internal void RefreshStats()
    {
        if (_bridge == null || !_bridge.IsOpen)
        {
            Stats = null;
            return;
        }
        
        var err = _bridge.GetStats(out var stats, DefaultTimeout);
        if (err == MCUSerialBridgeError.OK)
        {
            Stats = stats;
        }
        else
        {
            // 统计获取失败不影响连接状态，只清除缓存
            Stats = null;
        }
    }

    public static (string portName, uint baudRate) ParseUri(string uri)
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
