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
public record PortConfigSnapshot(string Type, uint Baud, uint? ReceiveFrameMs, uint? RetryTimeMs);

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
            result[kv.Key] = new NodeExportData
            {
                McuUri = entry.McuUri,
                NodeName = entry.NodeName,
                PortConfigs = entry.PortConfigs.Select(p => BuildPortConfigSnapshot(p)).ToArray(),
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

        // 构建结果
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
            // 可选：存储 iteration
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
        switch (typeid)
        {
            case 0:
                bw.Write(Convert.ToBoolean(val));
                break;
            case 1:
                bw.Write(Convert.ToByte(val));
                break;
            case 2:
                bw.Write(Convert.ToSByte(val));
                break;
            case 3:
                bw.Write(Convert.ToChar(val));
                break;
            case 4:
                bw.Write(Convert.ToInt16(val));
                break;
            case 5:
                bw.Write(Convert.ToUInt16(val));
                break;
            case 6:
                bw.Write(Convert.ToInt32(val));
                break;
            case 7:
                bw.Write(Convert.ToUInt32(val));
                break;
            case 8:
                bw.Write(Convert.ToSingle(val));
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
                configs.Add(new CANPortConfig(500000, 10));
            }
            else
            {
                configs.Add(new SerialPortConfig(9600, 20));
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
        if (entry.Stats != null)
        {
            var validPorts = entry.Stats.Value.GetValidPorts();
            statsSnapshot = new RuntimeStatsSnapshot(
                entry.Stats.Value.UptimeMs,
                entry.Stats.Value.DigitalInputs,
                entry.Stats.Value.DigitalOutputs,
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

        LayoutInfoSnapshot? layoutSnapshot = null;
        if (entry.Layout != null)
        {
            var validPorts = entry.Layout.Value.GetValidPorts();
            layoutSnapshot = new LayoutInfoSnapshot(
                (int)entry.Layout.Value.DigitalInputCount,
                (int)entry.Layout.Value.DigitalOutputCount,
                (int)entry.Layout.Value.PortCount,
                validPorts
                    .Select(p => new PortDescriptorSnapshot(p.Type.ToString(), p.Name))
                    .ToArray()
            );
        }

        return new NodeFullInfo(
            entry.UUID,
            entry.McuUri,
            entry.NodeName,
            versionSnapshot,
            layoutSnapshot,
            entry.PortConfigs.Select(p => BuildPortConfigSnapshot(p)).ToArray(),
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

    private static PortConfigSnapshot BuildPortConfigSnapshot(PortConfig p)
    {
        return p switch
        {
            SerialPortConfig s => new PortConfigSnapshot("Serial", s.Baud, s.ReceiveFrameMs, null),
            CANPortConfig c => new PortConfigSnapshot("CAN", c.Baud, null, c.RetryTimeMs),
            _ => new PortConfigSnapshot("Unknown", 0, null, null),
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
