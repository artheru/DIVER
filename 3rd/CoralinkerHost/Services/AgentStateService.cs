using CoralinkerSDK;

namespace CoralinkerHost.Services;

public sealed class AgentStateService
{
    private readonly ProjectStore _store;
    private readonly GitHistoryService _history;
    private readonly DiverBuildService _builder;
    private readonly RootRuntimeService _root;
    private readonly TerminalBroadcaster _terminal;
    private readonly FatalErrorStore _fatalErrors;

    public AgentStateService(
        ProjectStore store,
        GitHistoryService history,
        DiverBuildService builder,
        RootRuntimeService root,
        TerminalBroadcaster terminal,
        FatalErrorStore fatalErrors)
    {
        _store = store;
        _history = history;
        _builder = builder;
        _root = root;
        _terminal = terminal;
        _fatalErrors = fatalErrors;
    }

    public object GetCapabilities()
    {
        return new
        {
            ok = true,
            version = 1,
            recommendedClient = "HTTP snapshot/command APIs for Agents; SignalR is optional for live UI.",
            files = new
            {
                newProject = "POST /api/project/new",
                projectState = "GET /api/project",
                snapshot = "GET /api/files/snapshot",
                read = "GET /api/files/read?path=assets/inputs/Logic.cs",
                sync = "POST /api/files/sync",
                historyStatus = "GET /api/history/status",
                writableScopes = new[] { "assets/inputs/*.cs" },
                generatedScope = "assets/generated is read-only build output"
            },
            docs = new
            {
                index = "GET /api/docs/kit",
                resources = "GET /api/docs/kit/resources",
                bundle = "GET /api/docs/kit/bundle.zip",
                markdown = "GET /api/docs/kit/md/README.md",
                html = "GET /docs/kit/README.html"
            },
            buildRun = new
            {
                build = "POST /api/build",
                start = "POST /api/start",
                stop = "POST /api/stop",
                programNode = "POST /api/node/{uuid}/program",
                rootConfigure = "POST /api/root/configure",
                rootControlMeta = "GET /api/root/control/meta",
                rootControlSet = "POST /api/root/control/set"
            },
            nodes = new
            {
                probe = "POST /api/node/probe",
                add = "POST /api/node/add",
                addSimulated = "POST /api/node/add-simulated",
                configure = "POST /api/node/{uuid}/configure",
                remove = "POST /api/node/{uuid}/remove",
                list = "GET /api/nodes",
                state = "GET /api/nodes/state"
            },
            debug = new
            {
                agentState = "GET /api/agent/state",
                variables = "GET /api/variables",
                variablesMeta = "GET /api/variables/meta",
                variablesFlow = "GET /api/variables/flow",
                setVariable = "POST /api/variable/set",
                terminalLog = "GET /api/logs/terminal",
                buildLog = "GET /api/logs/build",
                nodeLog = "GET /api/logs/node/{uuid}",
                wireTapConfig = "GET /api/wiretap/configs",
                wireTapLogs = "GET /api/wiretap/logs",
                fatalError = "GET /api/errors/fatal"
            },
            polling = new
            {
                buildLogMs = 500,
                nodesStateMs = 500,
                variablesMs = 200,
                logsMs = 500,
                fatalErrorMs = 500
            }
        };
    }

    public object GetState()
    {
        _root.EnsureConfiguredRegistered();
        var session = DIVERSession.Instance;
        var project = _store.Get();
        var git = _history.GetStatus();
        var fields = session.GetAllCartFields();
        var metas = session.GetAllCartFieldMetas();

        return new
        {
            ok = true,
            project,
            git,
            build = new
            {
                isBuilding = _builder.IsBuilding,
                logTail = _terminal.GetBuildHistory().TakeLast(80).ToArray(),
                logics = ListLogicArtifacts()
            },
            session = new
            {
                state = session.State.ToString(),
                isRunning = session.IsRunning
            },
            nodes = new
            {
                info = session.GetNodeStates().Keys
                    .Select(uuid => session.GetNodeInfo(uuid))
                    .Where(info => info != null)
                    .ToArray(),
                state = session.GetNodeStates().Values.ToArray()
            },
            root = _root.GetState(),
            variables = new
            {
                meta = metas,
                values = fields.Values,
                flow = BuildVariablesFlow()
            },
            logs = new
            {
                terminalTail = _terminal.GetHistory().TakeLast(80).ToArray(),
                rootTail = _terminal.GetRootHistory().TakeLast(80).ToArray(),
                nodeIds = session.GetLoggedNodeIds()
            },
            fatalError = _fatalErrors.GetLatest()
        };
    }

    public VariablesFlowSnapshot BuildVariablesFlow()
    {
        var session = DIVERSession.Instance;
        _root.EnsureConfiguredRegistered();
        var states = session.GetNodeStates();
        var nodes = states.Values.Select(state =>
        {
            var info = session.GetNodeInfo(state.UUID);
            return new VariablesFlowNode(
                Id: state.UUID,
                NodeName: state.NodeName,
                McuUri: state.McuUri,
                LogicName: info?.LogicName,
                IsSimulated: (state.McuUri ?? "").StartsWith("sim://", StringComparison.OrdinalIgnoreCase),
                IsRoot: false
            );
        }).ToList();

        var rootState = _root.GetState();
        if (!string.IsNullOrWhiteSpace(rootState.LogicName))
        {
            nodes.Insert(0, new VariablesFlowNode("root-runtime", "Root Runtime", "root://runtime", rootState.LogicName, false, true));
        }

        var variables = session.GetAllCartFields().Values
            .Select(field => new VariablesFlowVariable(
                Name: field.Name,
                Type: field.Type ?? "Unknown",
                TypeId: field.TypeId,
                Direction: field.Direction ?? "none",
                Controllable: field.Controllable,
                SourceIds: field.SourceIds ?? Array.Empty<string>(),
                ReaderIds: field.ReaderIds ?? Array.Empty<string>(),
                WriterIds: field.WriterIds ?? Array.Empty<string>(),
                Value: field.Value
            ))
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new VariablesFlowSnapshot(nodes.ToArray(), variables);
    }

    private object[] ListLogicArtifacts()
    {
        if (!Directory.Exists(_store.GeneratedDir))
        {
            return Array.Empty<object>();
        }

        return Directory.GetFiles(_store.GeneratedDir, "*.bin")
            .Select(binPath =>
            {
                var name = Path.GetFileNameWithoutExtension(binPath);
                var jsonPath = binPath + ".json";
                return new
                {
                    name,
                    binPath = Path.GetFileName(binPath),
                    jsonPath = Path.GetFileName(jsonPath),
                    binSize = new FileInfo(binPath).Length,
                    jsonSize = File.Exists(jsonPath) ? new FileInfo(jsonPath).Length : 0
                };
            })
            .Cast<object>()
            .OrderBy(item => item.GetType().GetProperty("name")?.GetValue(item)?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed record VariablesFlowSnapshot(VariablesFlowNode[] Nodes, VariablesFlowVariable[] Variables);
public sealed record VariablesFlowNode(string Id, string NodeName, string? McuUri, string? LogicName, bool IsSimulated, bool IsRoot);
public sealed record VariablesFlowVariable(
    string Name,
    string Type,
    int TypeId,
    string Direction,
    bool Controllable,
    string[] SourceIds,
    string[] ReaderIds,
    string[] WriterIds,
    object? Value);
