using System.Collections.Concurrent;
using CoralinkerSDK;
using MCUSerialBridgeCLR;

namespace CoralinkerHost.Services;

public sealed class RuntimeSessionService
{
    private readonly TerminalBroadcaster _terminal;
    private readonly ProjectStore _store;
    private readonly DIVERSession _session = DIVERSession.Instance;

    private readonly object _gate = new();
    private BuildResult? _lastBuild;
    private string? _lastAsset;
    
    // Node log storage - each node has its own log buffer
    private readonly ConcurrentDictionary<string, NodeLogBuffer> _nodeLogs = new();
    private const int MaxLogLinesPerNode = 10000;

    public RuntimeSessionService(TerminalBroadcaster terminal, ProjectStore store)
    {
        _terminal = terminal;
        _store = store;
        
        // Subscribe to console output from nodes
        _session.OnConsoleOutput += HandleNodeConsoleOutput;
    }
    
    private void HandleNodeConsoleOutput(string nodeId, string message)
    {
        var buffer = _nodeLogs.GetOrAdd(nodeId, _ => new NodeLogBuffer(MaxLogLinesPerNode));
        buffer.Add(message);
        
        // Broadcast to node-specific log channel via SignalR
        _terminal.NodeLogLineAsync(nodeId, message).ConfigureAwait(false);
    }
    
    /// <summary>Get all node IDs that have logs</summary>
    public IReadOnlyList<string> GetLoggedNodeIds()
    {
        return _nodeLogs.Keys.ToArray();
    }
    
    /// <summary>Get log lines for a specific node (supports pagination)</summary>
    public NodeLogChunk GetNodeLogs(string nodeId, int offset = 0, int limit = 200)
    {
        if (!_nodeLogs.TryGetValue(nodeId, out var buffer))
        {
            return new NodeLogChunk(nodeId, Array.Empty<string>(), 0, 0, false);
        }
        
        return buffer.GetChunk(nodeId, offset, limit);
    }
    
    /// <summary>Clear logs for a specific node</summary>
    public void ClearNodeLogs(string nodeId)
    {
        if (_nodeLogs.TryRemove(nodeId, out _))
        {
            _terminal.LineAsync($"[logs] Cleared logs for node {nodeId}").ConfigureAwait(false);
        }
    }
    
    /// <summary>Clear all node logs</summary>
    public void ClearAllNodeLogs()
    {
        _nodeLogs.Clear();
        _terminal.LineAsync("[logs] Cleared all node logs").ConfigureAwait(false);
    }

    public RuntimeSessionSnapshot GetSnapshot()
    {
        var running = _session.State == DIVERSessionState.Running;
        lock (_gate)
        {
            return new RuntimeSessionSnapshot(running, _lastAsset, _lastBuild?.BuildRoot);
        }
    }

    /// <summary>Get all node states for frontend polling</summary>
    public IReadOnlyList<NodeStateInfo> GetAllNodeStates()
    {
        return _session.Nodes.Values
            .Select(node => new NodeStateInfo(
                node.NodeId,
                node.IsConnected,
                GetRunStateString(node),
                node.State?.IsConfigured != 0,
                node.State?.IsProgrammed != 0,
                node.State?.Mode.ToString() ?? "Unknown"
            ))
            .ToList();
    }

    /// <summary>Get node state by mcuUri</summary>
    public NodeStateInfo? GetNodeStateByUri(string mcuUri)
    {
        var node = _session.GetNodeByUri(mcuUri);
        
        if (node == null)
            return null;
        
        return new NodeStateInfo(
            node.NodeId,
            node.IsConnected,
            GetRunStateString(node),
            node.State?.IsConfigured != 0,
            node.State?.IsProgrammed != 0,
            node.State?.Mode.ToString() ?? "Unknown"
        );
    }
    
    /// <summary>Get standardized run state string (lowercase)</summary>
    private static string GetRunStateString(MCUNode node)
    {
        if (!node.IsConnected)
            return "offline";
            
        return node.State?.RunningState switch
        {
            MCUSerialBridgeCLR.MCURunState.Running => "running",
            MCUSerialBridgeCLR.MCURunState.Error => "error",
            _ => "idle"
        };
    }

    /// <summary>
    /// 添加节点到 DIVERSession 并连接（Probe 成功后调用）
    /// </summary>
    public async Task<MCUNode?> AddAndConnectNodeAsync(string nodeId, string mcuUri, CancellationToken ct)
    {
        await _terminal.LineAsync($"[session] Adding node {nodeId} to session...", ct);
        await EnsureBridgeDllAsync(ct);
        
        var node = _session.AddAndConnectNode(nodeId, mcuUri);
        
        if (node != null)
        {
            await _terminal.LineAsync($"[session] ✓ Node {nodeId} added and connected", ct);
        }
        else
        {
            await _terminal.LineAsync($"[session] ✗ Failed to add node {nodeId}", ct);
        }
        
        return node;
    }

    /// <summary>
    /// 从 DIVERSession 移除节点
    /// </summary>
    public async Task<bool> RemoveNodeAsync(string mcuUri, CancellationToken ct)
    {
        var node = _session.GetNodeByUri(mcuUri);
        if (node == null)
        {
            await _terminal.LineAsync($"[session] Node with mcuUri {mcuUri} not found", ct);
            return false;
        }
        
        var nodeId = node.NodeId;
        var removed = _session.RemoveNode(nodeId);
        
        if (removed)
        {
            await _terminal.LineAsync($"[session] ✓ Node {nodeId} removed", ct);
        }
        
        return removed;
    }

    /// <summary>
    /// 从 ProjectStore 恢复节点到 DIVERSession（用于项目加载后恢复连接）
    /// </summary>
    public async Task<RestoreNodesResult> RestoreNodesFromProjectAsync(CancellationToken ct)
    {
        var project = _store.Get();
        var graphNodes = ParseGraphNodes(project.NodeMap);
        
        // Filter MCU nodes (Vue Flow: coral-node, LiteGraph: coral/node)
        var mcuNodes = graphNodes.Where(n => 
            string.Equals(n.Type, "coral-node", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.Type, "coral/node", StringComparison.OrdinalIgnoreCase)).ToList();
        
        if (mcuNodes.Count == 0)
        {
            return new RestoreNodesResult(0, 0, new List<RestoreNodeInfo>());
        }
        
        await _terminal.LineAsync($"[session] Restoring {mcuNodes.Count} node(s) from project...", ct);
        await EnsureBridgeDllAsync(ct);
        
        var results = new List<RestoreNodeInfo>();
        int connected = 0;
        
        foreach (var gNode in mcuNodes)
        {
            var mcuUri = gNode.Properties.GetValueOrDefault("mcuUri") ?? "";
            if (string.IsNullOrWhiteSpace(mcuUri))
            {
                results.Add(new RestoreNodeInfo(gNode.Id, mcuUri, false, "Empty mcuUri"));
                continue;
            }
            
            // 检查节点是否已经在 session 中
            var existingNode = _session.GetNodeByUri(mcuUri);
            if (existingNode != null && existingNode.IsConnected)
            {
                results.Add(new RestoreNodeInfo(gNode.Id, mcuUri, true, "Already connected"));
                connected++;
                continue;
            }
            
            // 添加并连接节点
            var node = _session.AddAndConnectNode(gNode.Id, mcuUri);
            if (node != null && node.IsConnected)
            {
                var ver = node.Version?.ProductionName ?? "Unknown";
                results.Add(new RestoreNodeInfo(gNode.Id, mcuUri, true, $"Connected: {ver}"));
                connected++;
                await _terminal.LineAsync($"[session] ✓ {gNode.Id} restored: {ver}", ct);
            }
            else
            {
                var error = node?.LastError ?? "Connection failed";
                results.Add(new RestoreNodeInfo(gNode.Id, mcuUri, false, error));
                await _terminal.LineAsync($"[session] ✗ {gNode.Id} failed: {error}", ct);
            }
        }
        
        await _terminal.LineAsync($"[session] Restore complete: {connected}/{mcuNodes.Count} node(s) connected", ct);
        return new RestoreNodesResult(mcuNodes.Count, connected, results);
    }

    public void SetLastBuild(BuildResult build, string assetName)
    {
        lock (_gate)
        {
            _lastBuild = build;
            _lastAsset = assetName;
        }
    }

    public async Task<IReadOnlyList<NodeRuntimeInfo>> ConnectAsync(ProjectState project, CancellationToken ct)
    {
        await _terminal.LineAsync("[connect] ========== Starting Connection Process ==========", ct);
        await EnsureBridgeDllAsync(ct);
        var config = BuildSessionConfiguration(project);
        if (config.Nodes.Length == 0)
        {
            await _terminal.LineAsync("[connect] No MCU nodes in graph. Add nodes first.", ct);
            return Array.Empty<NodeRuntimeInfo>();
        }

        if (_session.State != DIVERSessionState.Idle)
        {
            await _terminal.LineAsync("[connect] Stopping previous session...", ct);
            _session.StopAll();
            _session.DisconnectAll();
            _session.ClearNodes();
            await _terminal.LineAsync("[connect] Previous session cleared.", ct);
        }

        await LogSessionPlanAsync(config, ct);
        
        await _terminal.LineAsync("[connect] Configuring session...", ct);
        _session.Configure(config);
        
        await _terminal.LineAsync("[connect] Connecting to nodes...", ct);
        var connected = _session.ConnectAll();
        
        await _terminal.LineAsync(
            $"[connect] Connection result: {connected}/{config.Nodes.Length} node(s) connected.",
            ct);
        
        foreach (var node in _session.Nodes.Values)
        {
            if (node.IsConnected)
            {
                var ver = node.Version;
                await _terminal.LineAsync(
                    $"[connect] ✓ {node.NodeId} connected: {ver?.ProductionName ?? "Unknown"} {ver?.GitTag ?? ""}",
                    ct);
            }
            else
            {
                await _terminal.LineAsync(
                    $"[connect] ✗ {node.NodeId} failed: {node.LastError ?? "Unknown error"}",
                    ct);
            }
        }
        
        if (connected == 0)
            throw new InvalidOperationException(
                "No nodes connected. Check mcuUri and device status.");

        await _terminal.LineAsync("[connect] ========== Connection Complete ==========", ct);
        
        // Connect only gets version info, no Configure/Program
        return _session.Nodes.Values
            .Select(node => NodeRuntimeInfo.FromNode(node))
            .ToArray();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _terminal.LineAsync("[start] ========== Starting Execution ==========", ct);
        
        if (_session.State == DIVERSessionState.Idle)
            throw new InvalidOperationException("Session not configured. Click Connect first.");

        // Configure and Program before starting
        await _terminal.LineAsync("[start] Configuring MCU ports and programming DIVER bytecode...", ct);
        var configured = _session.ConfigureAndProgramAll();
        
        await _terminal.LineAsync(
            $"[start] Configure & Program result: {configured}/{_session.Nodes.Count} node(s) successful.",
            ct);
        
        foreach (var node in _session.Nodes.Values)
        {
            if (!string.IsNullOrWhiteSpace(node.LastError))
                await _terminal.LineAsync(
                    $"[start] ✗ {node.NodeId}: {node.LastError}",
                    ct);
            else if (node.IsConnected)
                await _terminal.LineAsync(
                    $"[start] ✓ {node.NodeId}: Programmed ({node.ProgramBytes.Length:N0} bytes)",
                    ct);
        }
        
        if (configured == 0)
            throw new InvalidOperationException(
                "No nodes configured/programmed. Check MCU status.");

        await _terminal.LineAsync("[start] Starting DIVER execution on nodes...", ct);
        var started = _session.StartAll();
        
        await _terminal.LineAsync(
            $"[run] Started {started}/{_session.Nodes.Count} node(s).",
            ct);
        
        if (started == 0)
            throw new InvalidOperationException(
                "No nodes started. Check connection and MCU status.");
        
        await _terminal.LineAsync("[run] ========== Execution Running ==========", ct);
    }

    /// <summary>
    /// 完整启动流程：Configure → Program → Start
    /// 节点应该已经通过 Probe 添加并连接到 DIVERSession
    /// </summary>
    public async Task StartFullAsync(ProjectState project, CancellationToken ct)
    {
        await _terminal.LineAsync("[start] ========== Full Start Sequence ==========", ct);
        await EnsureBridgeDllAsync(ct);
        
        var config = BuildSessionConfiguration(project);
        
        if (config.Nodes.Length == 0)
        {
            throw new InvalidOperationException("No MCU nodes in graph. Add nodes first.");
        }

        // 检查是否有节点已经在 session 中（通过 Probe 添加）
        var connectedNodesCount = _session.Nodes.Values.Count(n => n.IsConnected);
        
        if (connectedNodesCount > 0)
        {
            // 节点已经通过 Probe 连接，使用 ConfigureConnectedNodes
            await _terminal.LineAsync($"[start] Found {connectedNodesCount} connected node(s) in session", ct);
            await _terminal.LineAsync("[start] Step 1/4: Configuring program for connected nodes...", ct);
            
            await LogSessionPlanAsync(config, ct);
            _session.ConfigureConnectedNodes(config);
        }
        else
        {
            // 没有已连接的节点，使用传统流程
            await _terminal.LineAsync("[start] No connected nodes, using traditional connect flow...", ct);
            await _terminal.LineAsync("[start] Step 1/4: Connecting to nodes...", ct);
            
            // Clear previous session if any
            if (_session.State != DIVERSessionState.Idle)
            {
                await _terminal.LineAsync("[start] Clearing previous session...", ct);
                _session.StopAll();
                _session.DisconnectAll();
                _session.ClearNodes();
            }

            await LogSessionPlanAsync(config, ct);
            
            _session.Configure(config);
            var connected = _session.ConnectAll();
            
            await _terminal.LineAsync(
                $"[start] Connection result: {connected}/{config.Nodes.Length} node(s) connected.",
                ct);
            
            foreach (var node in _session.Nodes.Values)
            {
                if (node.IsConnected)
                {
                    var ver = node.Version;
                    await _terminal.LineAsync(
                        $"[start] ✓ {node.NodeId} connected: {ver?.ProductionName ?? "Unknown"}",
                        ct);
                }
                else
                {
                    await _terminal.LineAsync(
                        $"[start] ✗ {node.NodeId} failed: {node.LastError ?? "Unknown error"}",
                        ct);
                }
            }
            
            if (connected == 0)
                throw new InvalidOperationException(
                    "No nodes connected. Check mcuUri and device status.");
        }

        // Step 2 & 3: Configure and Program
        await _terminal.LineAsync("[start] Step 2/4: Configuring MCU ports...", ct);
        await _terminal.LineAsync("[start] Step 3/4: Programming DIVER bytecode...", ct);
        
        var configured = _session.ConfigureAndProgramAll();
        
        await _terminal.LineAsync(
            $"[start] Configure & Program result: {configured}/{_session.Nodes.Count} node(s) successful.",
            ct);
        
        foreach (var node in _session.Nodes.Values)
        {
            if (!string.IsNullOrWhiteSpace(node.LastError))
                await _terminal.LineAsync(
                    $"[start] ✗ {node.NodeId}: {node.LastError}",
                    ct);
            else if (node.IsConnected)
                await _terminal.LineAsync(
                    $"[start] ✓ {node.NodeId}: Programmed ({node.ProgramBytes.Length:N0} bytes)",
                    ct);
        }
        
        if (configured == 0)
            throw new InvalidOperationException(
                "No nodes configured/programmed. Check MCU status.");

        // Step 4: Start execution
        await _terminal.LineAsync("[start] Step 4/4: Starting DIVER execution...", ct);
        var started = _session.StartAll();
        
        await _terminal.LineAsync(
            $"[start] Started {started}/{_session.Nodes.Count} node(s).",
            ct);
        
        if (started == 0)
            throw new InvalidOperationException(
                "No nodes started. Check connection and MCU status.");
        
        await _terminal.LineAsync("[start] ========== Execution Running ==========", ct);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _terminal.LineAsync("[stop] ========== Stopping Execution ==========", ct);
        _session.StopAll();
        await _terminal.LineAsync("[stop] All nodes stopped (connections preserved).", ct);
        // 不断开连接，保持节点在 session 中以便继续显示状态
        // _session.DisconnectAll();
        // _session.ClearNodes();
        await _terminal.LineAsync("[stop] ========== Stopped ==========", ct);
    }

    /// <summary>
    /// 完全停止并清空 session（断开所有连接）
    /// </summary>
    public async Task StopAndClearAsync(CancellationToken ct)
    {
        await _terminal.LineAsync("[stop] ========== Stopping and Clearing Session ==========", ct);
        _session.StopAll();
        await _terminal.LineAsync("[stop] All nodes stopped.", ct);
        _session.DisconnectAll();
        await _terminal.LineAsync("[stop] All nodes disconnected.", ct);
        _session.ClearNodes();
        await _terminal.LineAsync("[stop] Session cleared.", ct);
        await _terminal.LineAsync("[stop] ========== Stopped ==========", ct);
    }

    private SessionConfiguration BuildSessionConfiguration(ProjectState project)
    {
        var nodes = new List<NodeConfiguration>();
        
        // Parse nodeMap to extract node configurations (supports Vue Flow and LiteGraph formats)
        var graphNodes = ParseGraphNodes(project.NodeMap);
        
        // Filter MCU nodes (Vue Flow: coral-node, LiteGraph: coral/node)
        foreach (var node in graphNodes.Where(n => 
                     string.Equals(n.Type, "coral-node", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(n.Type, "coral/node", StringComparison.OrdinalIgnoreCase)))
        {
            var mcuUri = node.Properties.GetValueOrDefault("mcuUri") ?? "";
            var logicName = node.Properties.GetValueOrDefault("logicName") ?? "";
            
            if (string.IsNullOrWhiteSpace(mcuUri))
                throw new InvalidOperationException($"Node {node.Id} has empty mcuUri.");
            if (string.IsNullOrWhiteSpace(logicName))
                throw new InvalidOperationException($"Node {node.Id} has empty logicName.");

            var binPath = Path.Combine(_store.GeneratedDir, $"{logicName}.bin");
            var metaPath = Path.Combine(_store.GeneratedDir, $"{logicName}.bin.json");
            if (!File.Exists(binPath) || !File.Exists(metaPath))
                throw new InvalidOperationException(
                    $"Missing artifacts for '{logicName}'. Build first.");

            var diverPath = Path.Combine(_store.GeneratedDir, $"{logicName}.diver");
            var mapPath = Path.Combine(_store.GeneratedDir, $"{logicName}.diver.map.json");

            nodes.Add(new NodeConfiguration
            {
                NodeId = node.Id,
                McuUri = mcuUri,
                ProgramBytes = File.ReadAllBytes(binPath),
                MetaJson = File.ReadAllText(metaPath),
                DiverSrc = File.Exists(diverPath) ? File.ReadAllText(diverPath) : null,
                DiverMapJson = File.Exists(mapPath) ? File.ReadAllText(mapPath) : null,
                LogicName = logicName,
            });
        }

        return new SessionConfiguration
        {
            AssemblyPath = ResolveAssemblyPath(),
            Nodes = nodes.ToArray()
        };
    }
    
    /// <summary>
    /// Parse graph nodes from JSON - supports both Vue Flow format and legacy LiteGraph format
    /// </summary>
    private List<GraphNode> ParseGraphNodes(System.Text.Json.Nodes.JsonNode? nodeMap)
    {
        if (nodeMap == null)
            return new List<GraphNode>();
            
        try
        {
            var nodes = new List<GraphNode>();
            
            var nodesArray = nodeMap["nodes"]?.AsArray();
            if (nodesArray != null)
            {
                foreach (var nodeEl in nodesArray)
                {
                    if (nodeEl == null) continue;
                    
                    var type = nodeEl["type"]?.GetValue<string>();
                    
                    // 尝试获取 ID（Vue Flow 使用字符串，LiteGraph 使用数字）
                    string nodeId;
                    var idNode = nodeEl["id"];
                    if (idNode != null)
                    {
                        nodeId = idNode.ToString();
                    }
                    else
                    {
                        continue; // 跳过没有 ID 的节点
                    }
                    
                    var node = new GraphNode
                    {
                        Id = nodeId,
                        Type = type,
                        Properties = new Dictionary<string, string>()
                    };
                    
                    // Vue Flow 格式：属性在 data 中
                    var dataEl = nodeEl["data"];
                    if (dataEl != null)
                    {
                        foreach (var prop in dataEl.AsObject())
                        {
                            node.Properties[prop.Key] = prop.Value?.ToString() ?? "";
                        }
                    }
                    
                    // Legacy LiteGraph 格式：属性在 properties 中
                    var propsEl = nodeEl["properties"];
                    if (propsEl != null)
                    {
                        foreach (var prop in propsEl.AsObject())
                        {
                            // 不覆盖已有的属性
                            if (!node.Properties.ContainsKey(prop.Key))
                                node.Properties[prop.Key] = prop.Value?.ToString() ?? "";
                        }
                    }
                    
                    nodes.Add(node);
                }
            }
            
            return nodes;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RuntimeSession] Error parsing graph nodes: {ex.Message}");
            return new List<GraphNode>();
        }
    }
    
    private class GraphNode
    {
        public string Id { get; set; } = "";
        public string? Type { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new();
    }

    private string? ResolveAssemblyPath()
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_lastBuild?.OutputDllPath)
                && File.Exists(_lastBuild.OutputDllPath))
                return _lastBuild.OutputDllPath;
        }
        return null;
    }

    private async Task EnsureBridgeDllAsync(CancellationToken ct)
    {
        var baseDir = AppContext.BaseDirectory;
        var target = Path.Combine(baseDir, "mcu_serial_bridge.dll");
        if (File.Exists(target)) return;

        var candidate = Path.GetFullPath(
            Path.Combine(
                _store.HostRoot,
                "..",
                "..",
                "MCUSerialBridge",
                "build",
                "mcu_serial_bridge.dll"));
        if (File.Exists(candidate))
        {
            try
            {
                File.Copy(candidate, target, overwrite: true);
                await _terminal.LineAsync(
                    $"[connect] Copied native bridge DLL to output: {target}",
                    ct);
                return;
            }
            catch (Exception ex)
            {
                await _terminal.LineAsync(
                    $"[connect] Failed to copy native bridge DLL: {ex.Message}",
                    ct);
            }
        }

        await _terminal.LineAsync(
            $"[connect] Missing native bridge DLL. Expected: {target}",
            ct);
    }

    private async Task LogSessionPlanAsync(SessionConfiguration config, CancellationToken ct)
    {
        await _terminal.LineAsync(
            $"[connect] AssemblyPath: {config.AssemblyPath ?? "(none)"}",
            ct);
        await _terminal.LineAsync($"[connect] Nodes: {config.Nodes.Length}", ct);
        foreach (var node in config.Nodes)
        {
            await _terminal.LineAsync(
                $"[connect] node={node.NodeId} uri={node.McuUri} logic={node.LogicName ?? "(none)"} bytes={node.ProgramBytes.Length}",
                ct);
        }
    }
}

public sealed record RuntimeSessionSnapshot(bool IsRunning, string? AssetName, string? BuildRoot);

public sealed record NodeRuntimeInfo(
    string NodeId,
    string McuUri,
    VersionInfoDto? Version,
    MCUStateDto? State,
    PortConfigDto[] Ports,
    bool IsConnected,
    bool IsRunning)
{
    public static NodeRuntimeInfo FromNode(MCUNode node)
    {
        return new NodeRuntimeInfo(
            node.NodeId,
            node.McuUri,
            node.Version == null
                ? null
                : new VersionInfoDto(
                    node.Version.Value.ProductionName ?? "",
                    node.Version.Value.GitTag ?? "",
                    node.Version.Value.GitCommit ?? "",
                    node.Version.Value.BuildTime ?? ""),
            node.State == null
                ? null
                : new MCUStateDto(
                    node.State.Value.RunningState.ToString(),
                    node.State.Value.IsConfigured != 0,
                    node.State.Value.IsProgrammed != 0,
                    node.State.Value.Mode.ToString()),
            node.PortConfigs.Select(PortConfigDto.FromPortConfig).ToArray(),
            node.IsConnected,
            node.IsRunning);
    }
}

public sealed record VersionInfoDto(
    string ProductionName,
    string GitTag,
    string GitCommit,
    string BuildTime);

public sealed record MCUStateDto(
    string RunningState,
    bool IsConfigured,
    bool IsProgrammed,
    string Mode);

public sealed record PortConfigDto(
    string Type,
    uint Baud,
    uint? ReceiveFrameMs,
    uint? RetryTimeMs)
{
    public static PortConfigDto FromPortConfig(MCUSerialBridgeCLR.PortConfig config)
    {
        return config switch
        {
            MCUSerialBridgeCLR.SerialPortConfig serial => new PortConfigDto(
                "Serial", serial.Baud, serial.ReceiveFrameMs, null),
            MCUSerialBridgeCLR.CANPortConfig can => new PortConfigDto(
                "CAN", can.Baud, null, can.RetryTimeMs),
            _ => new PortConfigDto("Unknown", 0, null, null)
        };
    }
}

public sealed record RestoreNodesResult(
    int Total,
    int Connected,
    List<RestoreNodeInfo> Nodes);

public sealed record RestoreNodeInfo(
    string NodeId,
    string McuUri,
    bool Success,
    string Message);

/// <summary>Ring buffer for node log lines</summary>
internal sealed class NodeLogBuffer
{
    private readonly object _lock = new();
    private readonly List<string> _lines = new();
    private readonly int _maxLines;
    
    public NodeLogBuffer(int maxLines)
    {
        _maxLines = maxLines;
    }
    
    public void Add(string line)
    {
        lock (_lock)
        {
            _lines.Add($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
            // Trim if over limit
            if (_lines.Count > _maxLines)
            {
                _lines.RemoveRange(0, _lines.Count - _maxLines);
            }
        }
    }
    
    public NodeLogChunk GetChunk(string nodeId, int offset, int limit)
    {
        lock (_lock)
        {
            var total = _lines.Count;
            if (offset >= total)
            {
                return new NodeLogChunk(nodeId, Array.Empty<string>(), total, offset, false);
            }
            
            var actualLimit = Math.Min(limit, total - offset);
            var lines = _lines.Skip(offset).Take(actualLimit).ToArray();
            var hasMore = offset + actualLimit < total;
            
            return new NodeLogChunk(nodeId, lines, total, offset, hasMore);
        }
    }
    
    public int Count
    {
        get
        {
            lock (_lock) return _lines.Count;
        }
    }
}

/// <summary>A chunk of log lines for pagination</summary>
public sealed record NodeLogChunk(
    string NodeId,
    IReadOnlyList<string> Lines,
    int TotalLines,
    int Offset,
    bool HasMore);

/// <summary>Node state information for frontend polling</summary>
public sealed record NodeStateInfo(
    string NodeId,
    bool IsConnected,
    string RunState,
    bool IsConfigured,
    bool IsProgrammed,
    string Mode);
