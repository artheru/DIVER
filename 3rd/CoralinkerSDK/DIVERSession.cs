using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCUSerialBridgeCLR;

namespace CoralinkerSDK;

#region Data Transfer Objects

/// <summary>节点探测结果</summary>
public record NodeProbeResult(VersionInfo Version, LayoutInfo Layout);

/// <summary>节点设置（可修改部分）</summary>
public record NodeSettings
{
    public string? NodeName { get; init; }
    public PortConfig[]? PortConfigs { get; init; }
    public JsonObject? ExtraInfo { get; init; } // 用于存储位置等前端信息
}

/// <summary>节点状态快照</summary>
public record NodeStateSnapshot(
    string UUID,
    string McuUri,
    string NodeName,
    bool IsConnected,
    string RunState, // "idle" | "running" | "error" | "offline"
    bool IsConfigured,
    bool IsProgrammed,
    RuntimeStatsSnapshot? Stats
);

/// <summary>运行时统计快照</summary>
public record RuntimeStatsSnapshot(
    uint UptimeMs,
    uint DigitalInputs,
    uint DigitalOutputs,
    PortStatsSnapshot[] Ports
);

/// <summary>端口统计快照</summary>
public record PortStatsSnapshot(
    int Index,
    uint TxFrames,
    uint RxFrames,
    uint TxBytes,
    uint RxBytes
);

/// <summary>节点完整信息</summary>
public record NodeFullInfo(
    string UUID,
    string McuUri,
    string NodeName,
    VersionInfoSnapshot? Version,
    LayoutInfoSnapshot? Layout,
    PortConfigSnapshot[] PortConfigs,
    bool HasProgram,
    int ProgramSize,
    string? LogicName, // 程序名称，用于前端匹配
    CartFieldSnapshot[] CartFields,
    JsonObject? ExtraInfo
);

/// <summary>版本信息快照</summary>
public record VersionInfoSnapshot(
    string ProductionName,
    string GitTag,
    string GitCommit,
    string BuildTime
);

/// <summary>硬件布局快照</summary>
public record LayoutInfoSnapshot(
    int DigitalInputCount,
    int DigitalOutputCount,
    int PortCount,
    PortDescriptorSnapshot[] Ports
);

/// <summary>端口描述快照</summary>
public record PortDescriptorSnapshot(string Type, string Name);

/// <summary>端口配置快照</summary>
public record PortConfigSnapshot(string Type, string? Name, uint Baud, uint? ReceiveFrameMs, uint? RetryTimeMs);

/// <summary>Cart字段快照</summary>
public record CartFieldSnapshot(
    string Name,
    string Type,
    int TypeId,
    bool IsLowerIO,
    bool IsUpperIO,
    bool IsMutual
);

/// <summary>节点导出数据（用于持久化）</summary>
public record NodeExportData
{
    public string McuUri { get; init; } = "";
    public string NodeName { get; init; } = "";
    public LayoutInfoSnapshot? Layout { get; init; }
    public PortConfigSnapshot[]? PortConfigs { get; init; }
    public string? ProgramBase64 { get; init; }
    public string? MetaJson { get; init; }
    public string? LogicName { get; init; }
    public JsonObject? ExtraInfo { get; init; }
}

/// <summary>启动结果</summary>
public record StartResult(
    bool Success,
    int TotalNodes,
    int SuccessNodes,
    List<NodeStartError> Errors
);

/// <summary>节点启动错误</summary>
public record NodeStartError(string UUID, string NodeName, string Error);

/// <summary>Cart字段元信息（不含值，用于获取可绑定变量列表）</summary>
public record CartFieldMeta(
    string Name,
    string Type,
    int TypeId,
    bool IsLowerIO,
    bool IsUpperIO,
    bool IsMutual
);

/// <summary>Cart字段值</summary>
public record CartFieldValue(
    string Name,
    string Type,
    int TypeId,
    object? Value,
    bool IsLowerIO,
    bool IsUpperIO,
    bool IsMutual
);

/// <summary>日志条目</summary>
public record LogEntry(long Seq, DateTime Timestamp, string Message);

/// <summary>日志查询结果</summary>
public record LogQueryResult(string UUID, long LatestSeq, LogEntry[] Entries, bool HasMore);

#endregion

#region Internal Classes

/// <summary>节点条目 - 存储节点的所有信息</summary>
internal class NodeEntry : IDisposable
{
    // === 基本信息（不变量，Probe时获取）===
    public string UUID { get; set; } = "";
    public string McuUri { get; set; } = "";
    public VersionInfo? Version { get; set; }
    public LayoutInfo? Layout { get; set; }
    /// <summary>导入时恢复的 Layout 快照（当 Layout 为 null 时使用）</summary>
    public LayoutInfoSnapshot? ImportedLayoutSnapshot { get; set; }

    // === 用户设置（变量）===
    public string NodeName { get; set; } = "";
    public PortConfig[] PortConfigs { get; set; } = Array.Empty<PortConfig>();
    public JsonObject? ExtraInfo { get; set; } // 前端扩展信息（位置等）

    // === 代码（ProgramNode时设置）===
    public byte[] ProgramBytes { get; set; } = Array.Empty<byte>();
    public string? MetaJson { get; set; }
    public string? LogicName { get; set; }
    public CartFieldInfo[] CartFields { get; set; } = Array.Empty<CartFieldInfo>();

    // === 运行时状态（Start后才有）===
    public MCUNode? Handle { get; set; }
    public MCUState? State { get; set; }
    public RuntimeStats? Stats { get; set; }
    
    /// <summary>最后一次运行的统计数据（Stop后保留，用于显示TX/RX计数）</summary>
    public RuntimeStats? LastStats { get; set; }

    // === 日志 ===
    public NodeLogBuffer LogBuffer { get; } = new(10000);

    public bool IsConnected => Handle?.IsConnected ?? false;
    public bool IsRunning => Handle?.IsRunning ?? false;

    public void Dispose()
    {
        Handle?.Dispose();
        Handle = null;
    }
}

/// <summary>节点日志缓冲区</summary>
internal class NodeLogBuffer
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = new();
    private readonly int _maxLines;
    private long _seq = 0;

    public NodeLogBuffer(int maxLines)
    {
        _maxLines = maxLines;
    }

    public long Add(string message)
    {
        lock (_lock)
        {
            var entry = new LogEntry(++_seq, DateTime.Now, message);
            _entries.Add(entry);

            // 超出限制时删除旧日志
            if (_entries.Count > _maxLines)
            {
                _entries.RemoveRange(0, _entries.Count - _maxLines);
            }

            return _seq;
        }
    }

    public LogQueryResult Query(string uuid, long? afterSeq, int maxCount)
    {
        lock (_lock)
        {
            var latestSeq = _seq;

            if (_entries.Count == 0)
            {
                return new LogQueryResult(uuid, latestSeq, Array.Empty<LogEntry>(), false);
            }

            IEnumerable<LogEntry> filtered;
            if (afterSeq.HasValue)
            {
                // 获取 seq 大于 afterSeq 的日志
                filtered = _entries.Where(e => e.Seq > afterSeq.Value);
            }
            else
            {
                // 获取最新的 maxCount 条
                filtered = _entries.TakeLast(maxCount);
            }

            var result = filtered.Take(maxCount).ToArray();
            var hasMore = afterSeq.HasValue
                ? _entries.Count(e => e.Seq > afterSeq.Value) > maxCount
                : _entries.Count > maxCount;

            return new LogQueryResult(uuid, latestSeq, result, hasMore);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            // 不重置 seq，保持单调递增
        }
    }

    public long LatestSeq
    {
        get
        {
            lock (_lock)
                return _seq;
        }
    }
}

#endregion

/// <summary>
/// WireTap 标志（SDK 层定义，与底层 MCUSerialBridgeCLR.WireTapFlags 值一致）
/// </summary>
[Flags]
public enum WireTapFlags
{
    /// <summary>禁用</summary>
    None = 0x00,
    /// <summary>监听接收数据</summary>
    RX = 0x01,
    /// <summary>监听发送数据</summary>
    TX = 0x02,
    /// <summary>同时监听收发</summary>
    Both = RX | TX
}

/// <summary>
/// WireTap 端口配置
/// </summary>
public record WireTapPortConfig(byte PortIndex, WireTapFlags Flags);

/// <summary>
/// WireTap 数据事件参数
/// </summary>
public record WireTapDataEventArgs(
    string UUID,
    string NodeName,
    byte PortIndex,
    byte Direction,  // 0=RX, 1=TX
    PortType PortType,
    byte[] RawData,
    CANMessage? CANMessage  // 仅 CAN 端口有值
);

/// <summary>
/// WireTap 日志条目（存储在内存中）
/// </summary>
public record WireTapLogEntry(
    string UUID,
    string NodeName,
    byte PortIndex,
    byte Direction,  // 0=RX, 1=TX
    string PortType,  // "Serial" | "CAN"
    byte[] RawData,
    CANMessage? CANMessage,
    DateTime Timestamp
);

/// <summary>
/// DIVER 运行时会话管理器（单例）
/// 负责管理所有 MCU 节点的生命周期和数据交换
/// 设计为独立于网页Host，可被任意终端使用
/// </summary>
public sealed class DIVERSession : IDisposable
{
    private static readonly Lazy<DIVERSession> _instance = new(() => new DIVERSession());

    private readonly ConcurrentDictionary<string, NodeEntry> _nodes = new();
    private readonly ConcurrentDictionary<string, object?> _variables = new();
    private readonly object _stateLock = new();
    
    // WireTap 配置存储（独立于 PortConfig，可在 Start 前后修改）
    // Key: UUID, Value: 每端口的 WireTap 标志数组
    private readonly ConcurrentDictionary<string, WireTapFlags[]> _wireTapConfigs = new();
    
    // WireTap 日志存储（内存中，刷新后可恢复）
    // Key: UUID, Value: 该节点的日志列表
    private readonly ConcurrentDictionary<string, List<WireTapLogEntry>> _wireTapLogs = new();
    private readonly object _wireTapLogLock = new();
    private const int MaxWireTapLogEntries = 10000;

    // 后台线程
    private CancellationTokenSource? _workerCts;
    private Thread? _stateWorker;
    private Thread? _upperIOWorker;
    private readonly AutoResetEvent _upperIOSignal = new(false);
    private readonly ConcurrentDictionary<string, int> _upperIOPending = new();

    // 节点计数器（用于生成名称）
    private int _nodeIndex = 0;

    private bool _disposed;

    /// <summary>获取单例实例</summary>
    public static DIVERSession Instance => _instance.Value;

    /// <summary>会话状态</summary>
    public DIVERSessionState State { get; private set; } = DIVERSessionState.Idle;

    /// <summary>是否正在运行</summary>
    public bool IsRunning => State == DIVERSessionState.Running;

    /// <summary>状态变更事件</summary>
    public event Action<DIVERSessionState>? OnStateChanged;

    /// <summary>节点日志事件</summary>
    public event Action<string, string>? OnNodeLog;

    /// <summary>节点致命错误事件（MCU HardFault 或 ASSERT 失败），参数为 JSON 字符串</summary>
    public event Action<string, string>? OnFatalError;

    /// <summary>WireTap 数据事件（端口收发数据透视）</summary>
    public event Action<WireTapDataEventArgs>? OnWireTapData;

    private DIVERSession() { }

    #region 节点管理接口（只能在 Idle 状态调用）

    /// <summary>
    /// 探测节点 - Open → GetVersion/Layout → Close
    /// </summary>
    /// <param name="mcuUri">MCU 连接地址</param>
    /// <returns>基本信息或 null</returns>
    public NodeProbeResult? ProbeNode(string mcuUri)
    {
        MCUSerialBridge? bridge = null;
        try
        {
            var (portName, baudRate) = MCUNode.ParseUri(mcuUri);
            bridge = new MCUSerialBridge();

            var err = bridge.Open(portName, baudRate);
            if (err != MCUSerialBridgeError.OK)
            {
                Console.WriteLine($"[DIVERSession] Probe Open failed: {err.ToDescription()}");
                return null;
            }

            // Reset
            err = bridge.Reset(500);
            if (err != MCUSerialBridgeError.OK)
            {
                Console.WriteLine($"[DIVERSession] Probe Reset failed: {err.ToDescription()}");
                return null;
            }
            Thread.Sleep(MCUNode.ResetWaitTime);

            // Get Version
            err = bridge.GetVersion(out var version, 500);
            if (err != MCUSerialBridgeError.OK)
            {
                Console.WriteLine($"[DIVERSession] Probe GetVersion failed: {err.ToDescription()}");
                return null;
            }

            // Get Layout
            err = bridge.GetLayout(out var layout, 500);
            if (err != MCUSerialBridgeError.OK)
            {
                Console.WriteLine($"[DIVERSession] Probe GetLayout failed: {err.ToDescription()}");
                // Layout 失败不影响 Probe 结果，使用默认值
                layout = default;
            }

            return new NodeProbeResult(version, layout);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIVERSession] Probe exception: {ex.Message}");
            return null;
        }
        finally
        {
            bridge?.Dispose();
        }
    }

    /// <summary>
    /// 添加节点 - Probe成功后添加到管理列表
    /// </summary>
    /// <param name="mcuUri">MCU 连接地址</param>
    /// <returns>生成的 UUID 或 null</returns>
    public string? AddNode(string mcuUri)
    {
        EnsureIdle("AddNode");

        // 检查是否已存在相同 mcuUri 的节点
        if (
            _nodes.Values.Any(n =>
                string.Equals(n.McuUri, mcuUri, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            Console.WriteLine($"[DIVERSession] Node with mcuUri '{mcuUri}' already exists");
            return null;
        }

        // Probe
        var probeResult = ProbeNode(mcuUri);
        if (probeResult == null)
        {
            return null;
        }

        // 生成 UUID 和名称
        var uuid = Guid.NewGuid().ToString("N");
        var index = Interlocked.Increment(ref _nodeIndex);
        var nodeName = $"Node-{index}-{uuid[..8]}";

        // 根据 Layout 初始化端口配置
        var portConfigs = InitializePortConfigs(probeResult.Layout);

        var entry = new NodeEntry
        {
            UUID = uuid,
            McuUri = mcuUri,
            NodeName = nodeName,
            Version = probeResult.Version,
            Layout = probeResult.Layout,
            PortConfigs = portConfigs,
        };

        _nodes[uuid] = entry;
        Console.WriteLine($"[DIVERSession] Added node {nodeName} ({uuid}) at {mcuUri}");

        return uuid;
    }

    /// <summary>
    /// 删除节点
    /// </summary>
    public bool RemoveNode(string uuid)
    {
        EnsureIdle("RemoveNode");

        if (_nodes.TryRemove(uuid, out var entry))
        {
            entry.Dispose();
            _wireTapConfigs.TryRemove(uuid, out _);  // 清理 WireTap 配置
            Console.WriteLine($"[DIVERSession] Removed node {entry.NodeName}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 删除所有节点
    /// </summary>
    public void RemoveAllNodes()
    {
        EnsureIdle("RemoveAllNodes");

        foreach (var entry in _nodes.Values)
        {
            entry.Dispose();
        }
        _nodes.Clear();
        _variables.Clear();
        _wireTapConfigs.Clear();  // 清理 WireTap 配置
        _nodeIndex = 0;
        Console.WriteLine("[DIVERSession] Removed all nodes");
    }

    /// <summary>
    /// 修改节点设置
    /// </summary>
    public bool ConfigureNode(string uuid, NodeSettings settings)
    {
        EnsureIdle("ConfigureNode");

        if (!_nodes.TryGetValue(uuid, out var entry))
        {
            return false;
        }

        if (settings.NodeName != null)
        {
            entry.NodeName = settings.NodeName;
        }

        if (settings.PortConfigs != null)
        {
            entry.PortConfigs = settings.PortConfigs;
        }

        if (settings.ExtraInfo != null)
        {
            entry.ExtraInfo = settings.ExtraInfo;
        }

        Console.WriteLine($"[DIVERSession] Configured node {entry.NodeName}");
        return true;
    }

    #endregion

    #region WireTap 配置接口（可在 Idle 和 Running 状态调用）

    /// <summary>
    /// 设置节点的 WireTap 配置（可在 Start 前预设，也可在 Running 时动态修改）
    /// </summary>
    /// <param name="uuid">节点 UUID</param>
    /// <param name="portIndex">端口索引，0xFF = 全部端口</param>
    /// <param name="flags">WireTap 标志</param>
    /// <returns>是否成功</returns>
    public bool SetNodeWireTap(string uuid, byte portIndex, WireTapFlags flags)
    {
        if (!_nodes.TryGetValue(uuid, out var entry))
        {
            Console.WriteLine($"[DIVERSession] SetNodeWireTap: Node not found: {uuid}");
            return false;
        }

        // 确保有配置数组
        var configArray = _wireTapConfigs.GetOrAdd(uuid, _ => new WireTapFlags[16]);

        if (portIndex == 0xFF)
        {
            // 设置所有端口
            for (int i = 0; i < configArray.Length; i++)
            {
                configArray[i] = flags;
            }
            Console.WriteLine($"[DIVERSession] SetNodeWireTap {entry.NodeName}: ALL ports = {flags}");
        }
        else if (portIndex < configArray.Length)
        {
            configArray[portIndex] = flags;
            Console.WriteLine($"[DIVERSession] SetNodeWireTap {entry.NodeName}: Port[{portIndex}] = {flags}");
        }
        else
        {
            Console.WriteLine($"[DIVERSession] SetNodeWireTap: Invalid port index {portIndex}");
            return false;
        }

        // 如果已经在运行，立即应用到 MCU
        if (State == DIVERSessionState.Running && entry.Handle?.IsConnected == true)
        {
            var result = entry.Handle.SetWireTap(portIndex, flags);
            if (!result)
            {
                Console.WriteLine($"[DIVERSession] SetNodeWireTap: Failed to apply to MCU: {entry.Handle.LastError}");
                // 不返回 false，配置已保存，只是 MCU 应用失败
            }
        }

        return true;
    }

    /// <summary>
    /// 获取节点的 WireTap 配置
    /// </summary>
    /// <param name="uuid">节点 UUID</param>
    /// <returns>每端口的 WireTap 标志数组，或 null（如果节点不存在）</returns>
    public WireTapFlags[]? GetNodeWireTapConfig(string uuid)
    {
        if (!_nodes.ContainsKey(uuid))
            return null;
        
        return _wireTapConfigs.TryGetValue(uuid, out var config) ? config : null;
    }

    /// <summary>
    /// 获取所有节点的 WireTap 配置摘要
    /// </summary>
    public Dictionary<string, WireTapPortConfig[]> GetAllWireTapConfigs()
    {
        var result = new Dictionary<string, WireTapPortConfig[]>();
        
        foreach (var kv in _wireTapConfigs)
        {
            if (!_nodes.ContainsKey(kv.Key))
                continue;
            
            var configs = new List<WireTapPortConfig>();
            for (byte i = 0; i < kv.Value.Length; i++)
            {
                if (kv.Value[i] != WireTapFlags.None)
                {
                    configs.Add(new WireTapPortConfig(i, kv.Value[i]));
                }
            }
            
            if (configs.Count > 0)
            {
                result[kv.Key] = configs.ToArray();
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// 获取节点的 WireTap 日志
    /// </summary>
    /// <param name="uuid">节点 UUID</param>
    /// <param name="afterIndex">可选，只返回此索引之后的日志</param>
    /// <param name="maxCount">最大返回数量</param>
    /// <returns>日志条目数组和最新索引</returns>
    public (WireTapLogEntry[] Entries, int LatestIndex) GetNodeWireTapLogs(string uuid, int? afterIndex = null, int maxCount = 1000)
    {
        if (!_wireTapLogs.TryGetValue(uuid, out var logs))
            return (Array.Empty<WireTapLogEntry>(), 0);
        
        lock (_wireTapLogLock)
        {
            var startIndex = afterIndex.HasValue ? afterIndex.Value + 1 : 0;
            var count = Math.Min(maxCount, logs.Count - startIndex);
            
            if (count <= 0)
                return (Array.Empty<WireTapLogEntry>(), logs.Count - 1);
            
            var entries = logs.Skip(startIndex).Take(count).ToArray();
            return (entries, logs.Count - 1);
        }
    }
    
    /// <summary>
    /// 获取所有节点的 WireTap 日志
    /// </summary>
    public Dictionary<string, WireTapLogEntry[]> GetAllWireTapLogs()
    {
        var result = new Dictionary<string, WireTapLogEntry[]>();
        
        lock (_wireTapLogLock)
        {
            foreach (var kv in _wireTapLogs)
            {
                if (kv.Value.Count > 0)
                {
                    result[kv.Key] = kv.Value.ToArray();
                }
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// 清空所有 WireTap 日志（在 Start 时调用）
    /// </summary>
    public void ClearAllWireTapLogs()
    {
        lock (_wireTapLogLock)
        {
            foreach (var kv in _wireTapLogs)
            {
                kv.Value.Clear();
            }
        }
        Console.WriteLine("[DIVERSession] Cleared all WireTap logs");
    }
    
    /// <summary>
    /// 清空指定节点的 WireTap 日志
    /// </summary>
    public void ClearNodeWireTapLogs(string uuid)
    {
        if (_wireTapLogs.TryGetValue(uuid, out var logs))
        {
            lock (_wireTapLogLock)
            {
                logs.Clear();
            }
        }
    }

    #endregion

    #region 节点代码管理接口（只能在 Idle 状态调用）

    /// <summary>
    /// 设置节点代码（只存储，不下发）
    /// </summary>
    public bool ProgramNode(
        string uuid,
        byte[] programBytes,
        string metaJson,
        string? logicName = null
    )
    {
        EnsureIdle("ProgramNode");

        if (!_nodes.TryGetValue(uuid, out var entry))
        {
            return false;
        }

        entry.ProgramBytes = programBytes;
        entry.MetaJson = metaJson;
        entry.LogicName = logicName;

        // 解析 MetaJson
        entry.CartFields = HostRuntime.ParseMetaJson(metaJson);

        // 初始化变量存储
        foreach (var field in entry.CartFields)
        {
            if (!_variables.ContainsKey(field.Name))
            {
                _variables[field.Name] = HostRuntime.GetDefaultValue(field.TypeId);
            }
        }

        Console.WriteLine(
            $"[DIVERSession] Programmed node {entry.NodeName}: {programBytes.Length} bytes, {entry.CartFields.Length} fields, logic={logicName ?? "(none)"}"
        );
        return true;
    }

    /// <summary>
    /// 获取单个节点状态和统计
    /// </summary>
    public NodeStateSnapshot? GetNodeState(string uuid)
    {
        if (!_nodes.TryGetValue(uuid, out var entry))
        {
            return null;
        }

        return BuildNodeStateSnapshot(entry);
    }

    /// <summary>
    /// 获取单个节点完整信息
    /// </summary>
    public NodeFullInfo? GetNodeInfo(string uuid)
    {
        if (!_nodes.TryGetValue(uuid, out var entry))
        {
            return null;
        }

        return BuildNodeFullInfo(entry);
    }

    /// <summary>
    /// 获取所有节点状态
    /// </summary>
    public Dictionary<string, NodeStateSnapshot> GetNodeStates()
    {
        var result = new Dictionary<string, NodeStateSnapshot>();
        foreach (var kv in _nodes)
        {
            result[kv.Key] = BuildNodeStateSnapshot(kv.Value);
        }
        return result;
    }

    /// <summary>
    /// 导出所有节点（用于保存项目）
    /// </summary>
    public Dictionary<string, NodeExportData> ExportNodes()
    {
        var result = new Dictionary<string, NodeExportData>();
        foreach (var kv in _nodes)
        {
            var entry = kv.Value;
            var portNames = entry.Layout?.GetValidPorts().Select(p => p.Name).ToArray() ?? Array.Empty<string>();
            result[kv.Key] = new NodeExportData
            {
                McuUri = entry.McuUri,
                NodeName = entry.NodeName,
                Layout = BuildLayoutSnapshot(entry.Layout),
                PortConfigs = entry.PortConfigs.Select((p, i) => BuildPortConfigSnapshot(p, i < portNames.Length ? portNames[i] : null)).ToArray(),
                ProgramBase64 =
                    entry.ProgramBytes.Length > 0
                        ? Convert.ToBase64String(entry.ProgramBytes)
                        : null,
                MetaJson = entry.MetaJson,
                LogicName = entry.LogicName,
                ExtraInfo = entry.ExtraInfo,
            };
        }
        return result;
    }

    /// <summary>
    /// 导入节点（用于加载项目）
    /// </summary>
    public void ImportNodes(Dictionary<string, NodeExportData> data)
    {
        EnsureIdle("ImportNodes");

        // 清空现有节点
        foreach (var entry in _nodes.Values)
        {
            entry.Dispose();
        }
        _nodes.Clear();
        _variables.Clear();
        _wireTapConfigs.Clear();  // 清理 WireTap 配置

        // 导入新节点
        foreach (var kv in data)
        {
            var uuid = kv.Key;
            var d = kv.Value;

            var entry = new NodeEntry
            {
                UUID = uuid,
                McuUri = d.McuUri,
                NodeName = d.NodeName,
                ImportedLayoutSnapshot = d.Layout,  // 恢复导入的 Layout 快照
                PortConfigs =
                    d.PortConfigs?.Select(p => ParsePortConfig(p)).ToArray()
                    ?? Array.Empty<PortConfig>(),
                ProgramBytes = !string.IsNullOrEmpty(d.ProgramBase64)
                    ? Convert.FromBase64String(d.ProgramBase64)
                    : Array.Empty<byte>(),
                MetaJson = d.MetaJson,
                LogicName = d.LogicName,
                ExtraInfo = d.ExtraInfo,
            };

            // 解析 CartFields
            if (!string.IsNullOrEmpty(entry.MetaJson))
            {
                entry.CartFields = HostRuntime.ParseMetaJson(entry.MetaJson);

                // 初始化变量
                foreach (var field in entry.CartFields)
                {
                    if (!_variables.ContainsKey(field.Name))
                    {
                        _variables[field.Name] = HostRuntime.GetDefaultValue(field.TypeId);
                    }
                }
            }

            _nodes[uuid] = entry;

            // 更新节点计数器
            var match = System.Text.RegularExpressions.Regex.Match(entry.NodeName, @"Node-(\d+)-");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var idx))
            {
                if (idx > _nodeIndex)
                    _nodeIndex = idx;
            }
        }

        Console.WriteLine($"[DIVERSession] Imported {data.Count} nodes");
    }

    #endregion

    #region 会话管理接口

    /// <summary>
    /// 开始运行
    /// 内部流程: 检查代码 → Open → Configure → Program → Start → 启动状态轮询
    /// </summary>
    public StartResult Start()
    {
        EnsureIdle("Start");
        
        // 清空上一次运行的 WireTap 日志
        ClearAllWireTapLogs();
        
        // 清空上一次运行的统计数据
        foreach (var entry in _nodes.Values)
        {
            entry.LastStats = null;
        }

        var errors = new List<NodeStartError>();
        var successCount = 0;

        // 检查是否有节点
        if (_nodes.Count == 0)
        {
            return new StartResult(
                false,
                0,
                0,
                new List<NodeStartError> { new("", "", "No nodes configured") }
            );
        }

        // 检查所有节点是否都有代码
        foreach (var kv in _nodes)
        {
            if (kv.Value.ProgramBytes.Length == 0)
            {
                errors.Add(new NodeStartError(kv.Key, kv.Value.NodeName, "No program configured"));
            }
        }

        if (errors.Count > 0)
        {
            return new StartResult(false, _nodes.Count, 0, errors);
        }

        // 启动每个节点
        foreach (var kv in _nodes)
        {
            var entry = kv.Value;
            var error = StartNode(entry);

            if (error != null)
            {
                errors.Add(new NodeStartError(kv.Key, entry.NodeName, error));
            }
            else
            {
                successCount++;
            }
        }

        // 只要有一个成功，就切换到 Running 状态
        if (successCount > 0)
        {
            SetState(DIVERSessionState.Running);
            StartBackgroundWorkers();
        }

        return new StartResult(successCount > 0, _nodes.Count, successCount, errors);
    }

    /// <summary>
    /// 停止运行
    /// 内部流程: Reset → Close → 清理 Handle
    /// </summary>
    public void Stop()
    {
        if (State != DIVERSessionState.Running)
        {
            return;
        }

        // 停止后台线程
        StopBackgroundWorkers();

        // 停止并断开所有节点
        foreach (var entry in _nodes.Values)
        {
            try
            {
                // 保存最后的统计数据（用于 Stop 后仍能显示 TX/RX 计数）
                if (entry.Stats != null)
                {
                    entry.LastStats = entry.Stats;
                }
                
                entry.Handle?.Stop();
                entry.Handle?.Disconnect();
                entry.Handle?.Dispose();
                entry.Handle = null;
                entry.State = null;
                entry.Stats = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[DIVERSession] Error stopping node {entry.NodeName}: {ex.Message}"
                );
            }
        }

        SetState(DIVERSessionState.Idle);
        Console.WriteLine("[DIVERSession] Stopped");
    }

    private string? StartNode(NodeEntry entry)
    {
        try
        {
            // 创建 MCUNode
            var handle = new MCUNode(entry.UUID, entry.McuUri);
            handle.ProgramBytes = entry.ProgramBytes;
            handle.PortConfigs = entry.PortConfigs;
            handle.CartFields = entry.CartFields;

            // 注册回调
            handle.OnLowerIOReceived += data => HandleLowerIO(entry.UUID, data);
            handle.OnConsoleOutput += msg => HandleConsoleOutput(entry.UUID, msg);
            handle.OnFatalError += payload => HandleFatalError(entry.UUID, payload);

            // Connect
            if (!handle.Connect())
            {
                return $"Connect failed: {handle.LastError}";
            }

            entry.Version = handle.Version;
            entry.Layout = handle.Layout;

            // Configure
            if (!handle.Configure())
            {
                handle.Dispose();
                return $"Configure failed: {handle.LastError}";
            }

            // Program
            if (!handle.Program())
            {
                handle.Dispose();
                return $"Program failed: {handle.LastError}";
            }

            // 在 Start 之前应用预配置的 WireTap 设置（确保捕获启动时的数据）
            ApplyWireTapConfig(entry.UUID, handle);

            // 注册 WireTap 端口回调
            RegisterWireTapCallbacks(entry, handle);

            // Start
            if (!handle.Start())
            {
                handle.Dispose();
                return $"Start failed: {handle.LastError}";
            }

            entry.Handle = handle;
            entry.State = handle.State;

            Console.WriteLine($"[DIVERSession] Started node {entry.NodeName}");
            return null;
        }
        catch (Exception ex)
        {
            return $"Exception: {ex.Message}";
        }
    }

    #endregion

    #region 数据管理接口

    /// <summary>
    /// 获取所有 Cart 字段元信息（不需要 Start，从节点配置中获取）
    /// </summary>
    public List<CartFieldMeta> GetAllCartFieldMetas()
    {
        var result = new Dictionary<string, CartFieldMeta>(StringComparer.OrdinalIgnoreCase);
        
        // 添加每个节点的内置 __iteration 字段（LowerIO, int32）
        foreach (var entry in _nodes.Values)
        {
            var iterationName = $"{entry.NodeName}.__iteration";
            result[iterationName] = new CartFieldMeta(
                Name: iterationName,
                Type: "Int32",
                TypeId: 6, // TypeId for Int32
                IsLowerIO: true,
                IsUpperIO: false,
                IsMutual: false
            );
        }
        
        foreach (var entry in _nodes.Values)
        {
            foreach (var field in entry.CartFields)
            {
                if (!result.ContainsKey(field.Name))
                {
                    result[field.Name] = new CartFieldMeta(
                        field.Name,
                        HostRuntime.GetTypeName(field.TypeId),
                        field.TypeId,
                        field.IsLowerIO,
                        field.IsUpperIO,
                        field.IsMutual
                    );
                }
            }
        }
        
        return result.Values.ToList();
    }

    /// <summary>
    /// 获取所有 Cart 字段
    /// </summary>
    public Dictionary<string, CartFieldValue> GetAllCartFields()
    {
        var result = new Dictionary<string, CartFieldValue>();

        // 收集所有字段信息
        var fieldInfoMap = new Dictionary<string, CartFieldInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _nodes.Values)
        {
            foreach (var field in entry.CartFields)
            {
                if (!fieldInfoMap.ContainsKey(field.Name))
                {
                    fieldInfoMap[field.Name] = field;
                }
            }
        }

        // 添加每个节点的内置 __iteration 字段值
        foreach (var entry in _nodes.Values)
        {
            var iteration = HostRuntime.GetIteration(entry.UUID);
            var iterationName = $"{entry.NodeName}.__iteration";
            result[iterationName] = new CartFieldValue(
                Name: iterationName,
                Type: "Int32",
                TypeId: 6, // TypeId for Int32
                Value: iteration ?? 0,
                IsLowerIO: true,
                IsUpperIO: false,
                IsMutual: false
            );
        }

        // 构建其他变量结果
        foreach (var kv in _variables)
        {
            fieldInfoMap.TryGetValue(kv.Key, out var fieldInfo);

            result[kv.Key] = new CartFieldValue(
                kv.Key,
                fieldInfo != null ? HostRuntime.GetTypeName(fieldInfo.TypeId) : "Unknown",
                fieldInfo?.TypeId ?? 0,
                kv.Value,
                fieldInfo?.IsLowerIO ?? false,
                fieldInfo?.IsUpperIO ?? false,
                fieldInfo?.IsMutual ?? false
            );
        }

        return result;
    }

    /// <summary>
    /// 设置 Cart 字段值（只能设置非 LowerIO 的字段）
    /// </summary>
    public bool SetCartField(string fieldName, object value)
    {
        // 检查是否是 LowerIO 字段
        foreach (var entry in _nodes.Values)
        {
            var field = entry.CartFields.FirstOrDefault(f =>
                string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)
            );
            if (field != null && field.IsLowerIO)
            {
                Console.WriteLine($"[DIVERSession] Cannot set LowerIO field: {fieldName}");
                return false;
            }
        }

        _variables[fieldName] = value;
        return true;
    }

    /// <summary>
    /// 获取 Cart 字段值
    /// </summary>
    public object? GetCartField(string fieldName)
    {
        return _variables.TryGetValue(fieldName, out var value) ? value : null;
    }

    #endregion

    #region 日志接口

    /// <summary>
    /// 获取节点日志
    /// </summary>
    /// <param name="uuid">节点 UUID</param>
    /// <param name="afterSeq">获取 seq 大于此值的日志；null 表示获取最新的</param>
    /// <param name="maxCount">最大返回条数</param>
    public LogQueryResult? GetNodeLogs(string uuid, long? afterSeq = null, int maxCount = 200)
    {
        if (!_nodes.TryGetValue(uuid, out var entry))
        {
            return null;
        }

        return entry.LogBuffer.Query(uuid, afterSeq, maxCount);
    }

    /// <summary>
    /// 清空节点日志
    /// </summary>
    public void ClearNodeLogs(string uuid)
    {
        if (_nodes.TryGetValue(uuid, out var entry))
        {
            entry.LogBuffer.Clear();
        }
    }

    /// <summary>
    /// 清空所有日志
    /// </summary>
    public void ClearAllLogs()
    {
        foreach (var entry in _nodes.Values)
        {
            entry.LogBuffer.Clear();
        }
    }

    /// <summary>
    /// 获取有日志的节点列表
    /// </summary>
    public string[] GetLoggedNodeIds()
    {
        return _nodes.Keys.ToArray();
    }

    #endregion

    #region 后台线程

    private void StartBackgroundWorkers()
    {
        _workerCts = new CancellationTokenSource();

        // 状态轮询线程
        _stateWorker = new Thread(() => StatePollingLoop(_workerCts.Token))
        {
            IsBackground = true,
            Name = "DIVERSession-State",
        };
        _stateWorker.Start();

        // UpperIO 发送线程
        _upperIOWorker = new Thread(() => UpperIOLoop(_workerCts.Token))
        {
            IsBackground = true,
            Name = "DIVERSession-UpperIO",
        };
        _upperIOWorker.Start();
    }

    private void StopBackgroundWorkers()
    {
        _workerCts?.Cancel();
        _upperIOSignal.Set();

        try
        {
            _stateWorker?.Join(1000);
        }
        catch { }
        try
        {
            _upperIOWorker?.Join(1000);
        }
        catch { }

        _workerCts?.Dispose();
        _workerCts = null;
        _stateWorker = null;
        _upperIOWorker = null;
    }

    private void StatePollingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            foreach (var entry in _nodes.Values)
            {
                if (entry.Handle?.IsConnected == true)
                {
                    entry.Handle.RefreshState();
                    entry.State = entry.Handle.State;

                    if (entry.Handle.IsRunning)
                    {
                        entry.Handle.RefreshStats();
                        entry.Stats = entry.Handle.Stats;
                    }
                }
            }

            token.WaitHandle.WaitOne(500);
        }
    }

    private void UpperIOLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            _upperIOSignal.WaitOne(20);

            foreach (var kv in _upperIOPending.ToArray())
            {
                if (token.IsCancellationRequested)
                    break;
                if (kv.Value == 0)
                    continue;

                if (!_nodes.TryGetValue(kv.Key, out var entry))
                    continue;
                if (entry.Handle == null || !entry.Handle.IsRunning)
                    continue;

                _upperIOPending[kv.Key] = 0;

                var upper = SerializeUpperIO(entry);
                entry.Handle.SendUpperIO(upper, 20);
            }
        }
    }

    private void HandleLowerIO(string uuid, byte[] data)
    {
        if (!_nodes.TryGetValue(uuid, out var entry))
            return;

        try
        {
            // 反序列化 LowerIO 数据到变量存储
            DeserializeLowerIO(uuid, data, entry.CartFields);

            // 标记需要发送 UpperIO
            _upperIOPending[uuid] = 1;
            _upperIOSignal.Set();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIVERSession] LowerIO error from {entry.NodeName}: {ex.Message}");
        }
    }

    private void HandleConsoleOutput(string uuid, string message)
    {
        if (!_nodes.TryGetValue(uuid, out var entry))
            return;

        entry.LogBuffer.Add(message);
        OnNodeLog?.Invoke(uuid, message);
    }

    private void HandleFatalError(string uuid, ErrorPayload payload)
    {
        if (!_nodes.TryGetValue(uuid, out var entry))
            return;

        // 记录到日志
        var errorMsg = payload.Layout == CoreDumpLayout.String
            ? $"FATAL ERROR: {payload.GetErrorString()}"
            : $"FATAL ERROR: HardFault at IL={payload.DebugInfo.ILOffset}";
        
        entry.LogBuffer.Add(errorMsg);
        OnNodeLog?.Invoke(uuid, errorMsg);

        // 格式化错误信息为 JSON
        var errorJson = FormatFatalErrorJson(uuid, entry, payload);

        // 触发专用的 Fatal Error 事件
        OnFatalError?.Invoke(uuid, errorJson);

        Console.WriteLine($"[DIVERSession] Fatal error from {entry.NodeName}: {errorMsg}");

        // 断开节点连接，标记为离线
        DisconnectNodeOnFatalError(entry);
    }

    /// <summary>
    /// 应用预配置的 WireTap 设置到 MCU
    /// </summary>
    private void ApplyWireTapConfig(string uuid, MCUNode handle)
    {
        if (!_wireTapConfigs.TryGetValue(uuid, out var config))
            return;

        // 检查是否有任何端口启用了 WireTap
        bool hasAnyEnabled = false;
        for (int i = 0; i < config.Length; i++)
        {
            if (config[i] != WireTapFlags.None)
            {
                hasAnyEnabled = true;
                break;
            }
        }

        if (!hasAnyEnabled)
            return;

        // 检查是否所有端口配置相同（可以用一次调用设置全部）
        var firstFlags = config[0];
        bool allSame = true;
        for (int i = 1; i < config.Length; i++)
        {
            if (config[i] != firstFlags)
            {
                allSame = false;
                break;
            }
        }

        if (allSame && firstFlags != WireTapFlags.None)
        {
            // 全部端口相同，一次设置
            handle.SetWireTap(0xFF, firstFlags);
            Console.WriteLine($"[DIVERSession] Applied WireTap to all ports: {firstFlags}");
        }
        else
        {
            // 逐个端口设置
            for (byte i = 0; i < config.Length; i++)
            {
                if (config[i] != WireTapFlags.None)
                {
                    handle.SetWireTap(i, config[i]);
                    Console.WriteLine($"[DIVERSession] Applied WireTap to port[{i}]: {config[i]}");
                }
            }
        }
    }

    /// <summary>
    /// 注册 WireTap 端口数据回调
    /// </summary>
    private void RegisterWireTapCallbacks(NodeEntry entry, MCUNode handle)
    {
        // 获取端口布局信息
        var validPorts = handle.Layout?.GetValidPorts() ?? Array.Empty<PortDescriptor>();
        
        for (byte portIndex = 0; portIndex < validPorts.Length; portIndex++)
        {
            var port = validPorts[portIndex];
            var capturedPortIndex = portIndex; // 闭包捕获
            
            if (port.Type == MCUSerialBridgeCLR.PortType.CAN)
            {
                // CAN 端口回调
                handle.RegisterCANPortCallback(portIndex, (pi, direction, canMsg) =>
                {
                    HandleWireTapCANData(entry.UUID, pi, direction, canMsg);
                });
            }
            else
            {
                // Serial 端口回调
                handle.RegisterSerialPortCallback(portIndex, (pi, direction, data) =>
                {
                    HandleWireTapSerialData(entry.UUID, pi, direction, data);
                });
            }
        }
        
        Console.WriteLine($"[DIVERSession] Registered WireTap callbacks for {validPorts.Length} ports on {entry.NodeName}");
    }

    /// <summary>
    /// 处理 WireTap 串口数据
    /// </summary>
    private void HandleWireTapSerialData(string uuid, byte portIndex, byte direction, byte[] data)
    {
        if (!_nodes.TryGetValue(uuid, out var entry))
            return;

        var args = new WireTapDataEventArgs(
            UUID: uuid,
            NodeName: entry.NodeName,
            PortIndex: portIndex,
            Direction: direction,
            PortType: MCUSerialBridgeCLR.PortType.Serial,
            RawData: data,
            CANMessage: null
        );

        // 存储日志
        StoreWireTapLog(uuid, entry.NodeName, portIndex, direction, "Serial", data, null);

        OnWireTapData?.Invoke(args);
    }

    /// <summary>
    /// 处理 WireTap CAN 数据
    /// </summary>
    private void HandleWireTapCANData(string uuid, byte portIndex, byte direction, CANMessage canMsg)
    {
        if (!_nodes.TryGetValue(uuid, out var entry))
            return;

        var args = new WireTapDataEventArgs(
            UUID: uuid,
            NodeName: entry.NodeName,
            PortIndex: portIndex,
            Direction: direction,
            PortType: MCUSerialBridgeCLR.PortType.CAN,
            RawData: canMsg.ToBytes(),
            CANMessage: canMsg
        );

        // 存储日志
        StoreWireTapLog(uuid, entry.NodeName, portIndex, direction, "CAN", canMsg.ToBytes(), canMsg);

        OnWireTapData?.Invoke(args);
    }
    
    /// <summary>
    /// 存储 WireTap 日志条目
    /// </summary>
    private void StoreWireTapLog(string uuid, string nodeName, byte portIndex, byte direction, string portType, byte[] rawData, CANMessage? canMessage)
    {
        var logEntry = new WireTapLogEntry(
            UUID: uuid,
            NodeName: nodeName,
            PortIndex: portIndex,
            Direction: direction,
            PortType: portType,
            RawData: rawData,
            CANMessage: canMessage,
            Timestamp: DateTime.Now
        );
        
        var logs = _wireTapLogs.GetOrAdd(uuid, _ => new List<WireTapLogEntry>());
        
        lock (_wireTapLogLock)
        {
            logs.Add(logEntry);
            
            // 限制日志数量
            if (logs.Count > MaxWireTapLogEntries)
            {
                logs.RemoveRange(0, logs.Count - MaxWireTapLogEntries);
            }
        }
    }

    /// <summary>
    /// 发生致命错误后断开节点连接
    /// </summary>
    private void DisconnectNodeOnFatalError(NodeEntry entry)
    {
        try
        {
            if (entry.Handle != null)
            {
                entry.LogBuffer.Add("Disconnecting node due to fatal error...");
                OnNodeLog?.Invoke(entry.UUID, "Disconnecting node due to fatal error...");

                // 关闭连接（Dispose会断开）
                entry.Handle.Dispose();
                entry.Handle = null;

                // 清除运行时状态
                entry.State = null;
                entry.Stats = null;

                Console.WriteLine($"[DIVERSession] Node {entry.NodeName} disconnected due to fatal error");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIVERSession] Error disconnecting node {entry.NodeName}: {ex.Message}");
        }
    }

    private string FormatFatalErrorJson(string uuid, NodeEntry entry, ErrorPayload payload)
    {
        var errorData = new JsonObject
        {
            ["nodeUuid"] = uuid,
            ["nodeName"] = entry.NodeName ?? uuid[..8],
            ["logicName"] = entry.LogicName,
            ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            ["payloadVersion"] = (int)payload.PayloadVersion,
            ["version"] = new JsonObject
            {
                ["productionName"] = payload.Version.ProductionName,
                ["gitCommit"] = payload.Version.GitCommit,
                ["buildTime"] = payload.Version.BuildTime
            },
            ["debugInfo"] = new JsonObject
            {
                ["ilOffset"] = payload.DebugInfo.ILOffset,
                ["lineNo"] = payload.DebugInfo.LineNo
            },
            ["errorType"] = payload.Layout.ToString(),
            ["errorString"] = payload.Layout == CoreDumpLayout.String ? payload.GetErrorString() : null,
            ["coreDump"] = FormatCoreDumpJson(payload)
        };

        return errorData.ToJsonString();
    }

    private static JsonObject? FormatCoreDumpJson(ErrorPayload payload)
    {
        if (payload.Layout != CoreDumpLayout.STM32F4)
            return null;

        var f4 = payload.GetF4CoreDump();
        if (!f4.HasValue)
            return null;

        return new JsonObject
        {
            ["r0"] = f4.Value.R0,
            ["r1"] = f4.Value.R1,
            ["r2"] = f4.Value.R2,
            ["r3"] = f4.Value.R3,
            ["r12"] = f4.Value.R12,
            ["lr"] = f4.Value.LR,
            ["pc"] = f4.Value.PC,
            ["psr"] = f4.Value.PSR,
            ["msr"] = f4.Value.MSR,
            ["cfsr"] = f4.Value.CFSR,
            ["hfsr"] = f4.Value.HFSR,
            ["dfsr"] = f4.Value.DFSR,
            ["afsr"] = f4.Value.AFSR,
            ["bfar"] = f4.Value.BFAR,
            ["mmar"] = f4.Value.MMAR,
            ["msp"] = f4.Value.MSP,
            ["stackEnd"] = f4.Value.StackEnd
        };
    }

    #endregion

    #region 序列化/反序列化

    private byte[] SerializeUpperIO(NodeEntry entry)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        foreach (var field in entry.CartFields)
        {
            if (!field.IsUpperIO && !field.IsMutual)
                continue;

            bw.Write(field.TypeId);
            var val = _variables.TryGetValue(field.Name, out var v)
                ? v
                : HostRuntime.GetDefaultValue(field.TypeId);
            WriteTypedValue(bw, field.TypeId, val);
        }

        return ms.ToArray();
    }

    private void DeserializeLowerIO(string uuid, byte[] data, CartFieldInfo[] fields)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        // LowerIO 以 iteration (int32) 开头
        if (ms.Length >= 4)
        {
            var iteration = br.ReadInt32();
            // 存储 iteration 到 HostRuntime
            HostRuntime.SetCartVariable(uuid, "__iteration", iteration);
        }

        foreach (var field in fields)
        {
            if (field.IsUpperIO)
                continue;
            if (ms.Position >= data.Length)
                break;

            var typeid = br.ReadByte();
            var value = ReadTypedValue(br, typeid);
            if (value != null)
            {
                _variables[field.Name] = value;
            }
        }
    }

    private static object? ReadTypedValue(BinaryReader br, int typeid)
    {
        return typeid switch
        {
            0 => br.ReadBoolean(),
            1 => br.ReadByte(),
            2 => br.ReadSByte(),
            3 => br.ReadChar(),
            4 => br.ReadInt16(),
            5 => br.ReadUInt16(),
            6 => br.ReadInt32(),
            7 => br.ReadUInt32(),
            8 => br.ReadSingle(),
            _ => null,
        };
    }

    private static void WriteTypedValue(BinaryWriter bw, int typeid, object? val)
    {
        val ??= HostRuntime.GetDefaultValue(typeid);
        
        // 将值转换为 double，统一处理各种数值类型（包括 JsonElement）
        double numVal = 0;
        if (val != null)
        {
            if (val is System.Text.Json.JsonElement je)
            {
                // JsonElement 需要特殊处理
                if (je.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    numVal = je.GetDouble();
                }
                else if (je.ValueKind == System.Text.Json.JsonValueKind.True)
                {
                    numVal = 1;
                }
                else if (je.ValueKind == System.Text.Json.JsonValueKind.False)
                {
                    numVal = 0;
                }
            }
            else
            {
                try
                {
                    numVal = Convert.ToDouble(val);
                }
                catch
                {
                    numVal = 0;
                }
            }
        }
        
        switch (typeid)
        {
            case 0: // bool
                bw.Write(numVal != 0);
                break;
            case 1: // byte
                bw.Write((byte)Math.Clamp(Math.Round(numVal), byte.MinValue, byte.MaxValue));
                break;
            case 2: // sbyte
                bw.Write((sbyte)Math.Clamp(Math.Round(numVal), sbyte.MinValue, sbyte.MaxValue));
                break;
            case 3: // char
                bw.Write((char)Math.Clamp(Math.Round(numVal), char.MinValue, char.MaxValue));
                break;
            case 4: // int16
                bw.Write((short)Math.Clamp(Math.Round(numVal), short.MinValue, short.MaxValue));
                break;
            case 5: // uint16
                bw.Write((ushort)Math.Clamp(Math.Round(numVal), ushort.MinValue, ushort.MaxValue));
                break;
            case 6: // int32
                bw.Write((int)Math.Clamp(Math.Round(numVal), int.MinValue, int.MaxValue));
                break;
            case 7: // uint32
                bw.Write((uint)Math.Clamp(Math.Round(numVal), uint.MinValue, uint.MaxValue));
                break;
            case 8: // float
                bw.Write((float)numVal);
                break;
        }
    }

    #endregion

    #region 辅助方法

    private void EnsureIdle(string operation)
    {
        if (State != DIVERSessionState.Idle)
        {
            throw new InvalidOperationException($"Cannot {operation} while session is {State}");
        }
    }

    private void SetState(DIVERSessionState state)
    {
        State = state;
        OnStateChanged?.Invoke(state);
    }

    private static PortConfig[] InitializePortConfigs(LayoutInfo layout)
    {
        var validPorts = layout.GetValidPorts();
        if (validPorts.Length == 0)
        {
            return HostRuntime.DefaultPortConfigs;
        }

        var configs = new List<PortConfig>();
        foreach (var port in validPorts)
        {
            if (port.Type == PortType.CAN)
            {
                // CAN: 默认 1000000 波特率, 10ms 重发
                configs.Add(new CANPortConfig(1000000, 10));
            }
            else
            {
                // Serial: 默认 115200 波特率, 0ms 帧间隔
                configs.Add(new SerialPortConfig(115200, 0));
            }
        }
        return configs.ToArray();
    }

    private static NodeStateSnapshot BuildNodeStateSnapshot(NodeEntry entry)
    {
        var runState = "offline";
        if (entry.Handle?.IsConnected == true)
        {
            runState = entry.State?.RunningState switch
            {
                MCURunState.Running => "running",
                MCURunState.Error => "error",
                _ => "idle",
            };
        }

        RuntimeStatsSnapshot? statsSnapshot = null;
        // 使用当前统计，如果为空则使用最后一次运行的统计（Stop 后保留 TX/RX 计数）
        var statsSource = entry.Stats ?? entry.LastStats;
        if (statsSource != null)
        {
            var validPorts = statsSource.Value.GetValidPorts();
            statsSnapshot = new RuntimeStatsSnapshot(
                statsSource.Value.UptimeMs,
                statsSource.Value.DigitalInputs,
                statsSource.Value.DigitalOutputs,
                validPorts
                    .Select(
                        (p, i) =>
                            new PortStatsSnapshot(i, p.TxFrames, p.RxFrames, p.TxBytes, p.RxBytes)
                    )
                    .ToArray()
            );
        }

        return new NodeStateSnapshot(
            entry.UUID,
            entry.McuUri,
            entry.NodeName,
            entry.Handle?.IsConnected ?? false,
            runState,
            entry.State?.IsConfigured != 0,
            entry.State?.IsProgrammed != 0,
            statsSnapshot
        );
    }

    private static NodeFullInfo BuildNodeFullInfo(NodeEntry entry)
    {
        VersionInfoSnapshot? versionSnapshot = null;
        if (entry.Version != null)
        {
            versionSnapshot = new VersionInfoSnapshot(
                entry.Version.Value.ProductionName ?? "",
                entry.Version.Value.GitTag ?? "",
                entry.Version.Value.GitCommit ?? "",
                entry.Version.Value.BuildTime ?? ""
            );
        }

        // 优先使用实际 Layout，否则使用导入的 Layout 快照
        var layoutSnapshot = BuildLayoutSnapshot(entry.Layout) ?? entry.ImportedLayoutSnapshot;

        // 从 Layout 或导入的快照获取端口名称
        var portNames = entry.Layout?.GetValidPorts().Select(p => p.Name).ToArray()
            ?? entry.ImportedLayoutSnapshot?.Ports.Select(p => p.Name).ToArray()
            ?? Array.Empty<string>();
        
        return new NodeFullInfo(
            entry.UUID,
            entry.McuUri,
            entry.NodeName,
            versionSnapshot,
            layoutSnapshot,
            entry.PortConfigs.Select((p, i) => BuildPortConfigSnapshot(p, i < portNames.Length ? portNames[i] : null)).ToArray(),
            entry.ProgramBytes.Length > 0,
            entry.ProgramBytes.Length,
            entry.LogicName,
            entry
                .CartFields.Select(f => new CartFieldSnapshot(
                    f.Name,
                    HostRuntime.GetTypeName(f.TypeId),
                    f.TypeId,
                    f.IsLowerIO,
                    f.IsUpperIO,
                    f.IsMutual
                ))
                .ToArray(),
            entry.ExtraInfo
        );
    }

    private static PortConfigSnapshot BuildPortConfigSnapshot(PortConfig p, string? name)
    {
        return p switch
        {
            SerialPortConfig s => new PortConfigSnapshot("Serial", name, s.Baud, s.ReceiveFrameMs, null),
            CANPortConfig c => new PortConfigSnapshot("CAN", name, c.Baud, null, c.RetryTimeMs),
            _ => new PortConfigSnapshot("Unknown", name, 0, null, null),
        };
    }

    private static PortConfig ParsePortConfig(PortConfigSnapshot p)
    {
        if (string.Equals(p.Type, "CAN", StringComparison.OrdinalIgnoreCase))
        {
            return new CANPortConfig(p.Baud, p.RetryTimeMs ?? 10);
        }
        return new SerialPortConfig(p.Baud, p.ReceiveFrameMs ?? 0);
    }

    private static LayoutInfoSnapshot? BuildLayoutSnapshot(LayoutInfo? layout)
    {
        if (layout == null) return null;
        
        var validPorts = layout.Value.GetValidPorts();
        return new LayoutInfoSnapshot(
            (int)layout.Value.DigitalInputCount,
            (int)layout.Value.DigitalOutputCount,
            (int)layout.Value.PortCount,
            validPorts
                .Select(p => new PortDescriptorSnapshot(p.Type.ToString(), p.Name))
                .ToArray()
        );
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Stop();

        foreach (var entry in _nodes.Values)
        {
            entry.Dispose();
        }
        _nodes.Clear();
        _variables.Clear();

        _upperIOSignal.Dispose();
    }
}

/// <summary>会话状态</summary>
public enum DIVERSessionState
{
    /// <summary>空闲（可配置）</summary>
    Idle,

    /// <summary>运行中</summary>
    Running,
}
