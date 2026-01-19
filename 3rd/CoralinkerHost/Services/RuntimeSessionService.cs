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

    public RuntimeSessionService(TerminalBroadcaster terminal, ProjectStore store)
    {
        _terminal = terminal;
        _store = store;
    }

    public RuntimeSessionSnapshot GetSnapshot()
    {
        var running = _session.State == DIVERSessionState.Running;
        lock (_gate)
        {
            return new RuntimeSessionSnapshot(running, _lastAsset, _lastBuild?.BuildRoot);
        }
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
        await EnsureBridgeDllAsync(ct);
        var config = BuildSessionConfiguration(project);
        if (config.Nodes.Length == 0)
        {
            await _terminal.LineAsync("[connect] No MCU nodes in graph.", ct);
            return Array.Empty<NodeRuntimeInfo>();
        }

        if (_session.State != DIVERSessionState.Idle)
        {
            _session.StopAll();
            _session.DisconnectAll();
            _session.ClearNodes();
        }

        await LogSessionPlanAsync(config, ct);
        _session.Configure(config);
        var connected = _session.ConnectAll();
        await _terminal.LineAsync(
            $"[connect] Connected {connected}/{config.Nodes.Length} node(s).",
            ct);
        foreach (var node in _session.Nodes.Values)
        {
            if (!node.IsConnected && !string.IsNullOrWhiteSpace(node.LastError))
                await _terminal.LineAsync(
                    $"[connect] {node.NodeId} failed: {node.LastError}",
                    ct);
        }
        if (connected == 0)
            throw new InvalidOperationException(
                "No nodes connected. Check mcuUri and device status.");

        // Connect only gets version info, no Configure/Program
        return _session.Nodes.Values
            .Select(node => NodeRuntimeInfo.FromNode(node))
            .ToArray();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_session.State == DIVERSessionState.Idle)
            throw new InvalidOperationException("Session not configured. Click Connect first.");

        // Configure and Program before starting
        var configured = _session.ConfigureAndProgramAll();
        await _terminal.LineAsync(
            $"[start] Configured & Programmed {configured}/{_session.Nodes.Count} node(s).",
            ct);
        foreach (var node in _session.Nodes.Values)
        {
            if (!string.IsNullOrWhiteSpace(node.LastError))
                await _terminal.LineAsync(
                    $"[start] {node.NodeId} error: {node.LastError}",
                    ct);
        }
        if (configured == 0)
            throw new InvalidOperationException(
                "No nodes configured/programmed. Check MCU status.");

        var started = _session.StartAll();
        await _terminal.LineAsync(
            $"[run] Started {started}/{_session.Nodes.Count} node(s).",
            ct);
        if (started == 0)
            throw new InvalidOperationException(
                "No nodes started. Check connection and MCU status.");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _session.StopAll();
        _session.DisconnectAll();
        _session.ClearNodes();
        await _terminal.LineAsync("[run] Stopped.", ct);
    }

    private SessionConfiguration BuildSessionConfiguration(ProjectState project)
    {
        var nodes = new List<NodeConfiguration>();
        foreach (var node in project.Nodes.Where(n =>
                     !string.Equals(n.Kind, "root", StringComparison.OrdinalIgnoreCase)))
        {
            var mcuUri = node.Properties.TryGetValue("mcuUri", out var u) ? u : "";
            var logicName = node.Properties.TryGetValue("logicName", out var ln) ? ln : "";
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
