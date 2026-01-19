using System.Globalization;
using CoralinkerSDK;
using MCUSerialBridgeCLR;

namespace CoralinkerHost.Services;

public sealed class VariableInspectorPushService : BackgroundService
{
    private readonly RuntimeSessionService _runtime;
    private readonly TerminalBroadcaster _terminal;

    public VariableInspectorPushService(RuntimeSessionService runtime, TerminalBroadcaster terminal)
    {
        _runtime = runtime;
        _terminal = terminal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastNodePush = DateTime.UtcNow;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = BuildSnapshot();
                if (snapshot != null)
                    await _terminal.VarsSnapshotAsync(snapshot, stoppingToken);
            }
            catch
            {
                // ignore polling errors
            }

            if ((DateTime.UtcNow - lastNodePush).TotalMilliseconds >= 1000)
            {
                try
                {
                    var nodeSnapshot = BuildNodeSnapshot();
                    if (nodeSnapshot != null)
                        await _terminal.NodeSnapshotAsync(nodeSnapshot, stoppingToken);
                }
                catch
                {
                    // ignore polling errors
                }

                lastNodePush = DateTime.UtcNow;
            }

            await Task.Delay(250, stoppingToken);
        }
    }

    private static object? BuildSnapshot()
    {
        var session = DIVERSession.Instance;
        if (session.Nodes.Count == 0) return null;

        var items = new List<object>();
        var fieldMap = new Dictionary<string, CartFieldInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in session.Nodes.Values)
        {
            foreach (var field in node.CartFields)
            {
                if (!fieldMap.ContainsKey(field.Name))
                    fieldMap[field.Name] = field;
            }
        }

        var vars = HostRuntime.GetAllVariables();
        foreach (var kv in vars)
        {
            if (string.Equals(kv.Key, "__iteration", StringComparison.OrdinalIgnoreCase))
                continue;

            fieldMap.TryGetValue(kv.Key, out var field);
            var typeName = field != null
                ? HostRuntime.GetTypeName(field.TypeId)
                : (kv.Value?.GetType().Name ?? "object");
            var (dir, icon) = field switch
            {
                { IsUpperIO: true } => ("upper", "arrow-up"),
                { IsLowerIO: true } => ("lower", "arrow-down"),
                { IsMutual: true } => ("mutual", "arrow-up"),
                _ => ("none", "circle")
            };

            items.Add(new
            {
                name = kv.Key,
                type = typeName,
                direction = dir,
                icon,
                value = FormatValue(kv.Value)
            });
        }

        foreach (var node in session.Nodes.Values)
        {
            var iteration = HostRuntime.GetIteration(node.NodeId);
            if (iteration == null) continue;
            items.Add(new
            {
                name = $"{node.NodeId}.__iteration",
                type = "Int32",
                direction = "lower",
                icon = "arrow-down",
                value = iteration.Value.ToString()
            });
        }

        return new
        {
            targetType = "DIVERSession",
            fields = items
        };
    }

    private static object? BuildNodeSnapshot()
    {
        var session = DIVERSession.Instance;
        if (session.Nodes.Count == 0) return null;

        var nodes = session.Nodes.Values.Select(node =>
        {
            var runState = "offline";
            if (node.IsConnected)
            {
                runState = node.State?.RunningState switch
                {
                    MCURunState.Running => "running",
                    MCURunState.Error => "error",
                    _ => "idle"
                };
            }

            var layout = node.Layout;
            var layoutPorts = layout?.GetValidPorts() ?? Array.Empty<PortDescriptor>();

            // Debug logging for layout ports
            if (layoutPorts.Length > 0)
            {
                Console.WriteLine($"[VariableInspector] Node {node.NodeId}: Layout has {layoutPorts.Length} ports");
                for (int idx = 0; idx < layoutPorts.Length; idx++)
                {
                    var p = layoutPorts[idx];
                    Console.WriteLine($"[VariableInspector]   Port[{idx}]: Type={(int)p.Type}({p.Type}), Name='{p.Name}'");
                }
            }

            return new
            {
                nodeId = node.NodeId,
                isConnected = node.IsConnected,
                runState,
                isConfigured = node.State?.IsConfigured != 0,
                isProgrammed = node.State?.IsProgrammed != 0,
                mode = node.State?.Mode.ToString() ?? "Unknown",
                version = node.Version == null
                    ? null
                    : new
                    {
                        productionName = node.Version.Value.ProductionName ?? "",
                        gitTag = node.Version.Value.GitTag ?? "",
                        gitCommit = node.Version.Value.GitCommit ?? "",
                        buildTime = node.Version.Value.BuildTime ?? ""
                    },
                layout = layout == null
                    ? null
                    : new
                    {
                        digitalInputCount = (int)layout.Value.DigitalInputCount,
                        digitalOutputCount = (int)layout.Value.DigitalOutputCount,
                        portCount = (int)layout.Value.PortCount
                    },
                ports = node.PortConfigs.Select((p, i) =>
                {
                    var portName = i < layoutPorts.Length ? layoutPorts[i].Name : $"Port{i}";
                    var portType = i < layoutPorts.Length ? layoutPorts[i].Type.ToString() : "Unknown";
                    return new
                    {
                        index = i,
                        name = portName,
                        layoutType = portType,
                        type = p switch
                        {
                            SerialPortConfig s => "Serial",
                            CANPortConfig c => "CAN",
                            _ => "Unknown"
                        },
                        baud = p switch
                        {
                            SerialPortConfig s => s.Baud,
                            CANPortConfig c => c.Baud,
                            _ => 0u
                        },
                        extra = p switch
                        {
                            SerialPortConfig s => $"FrameMs={s.ReceiveFrameMs}",
                            CANPortConfig c => $"RetryMs={c.RetryTimeMs}",
                            _ => ""
                        }
                    };
                }).ToArray()
            };
        }).ToArray();

        return new { nodes };
    }

    private static string FormatValue(object? v)
    {
        if (v == null) return "null";
        if (v is string s) return s;
        if (v is Array a)
        {
            var parts = new List<string>();
            foreach (var e in a) parts.Add(FormatValue(e));
            return "[" + string.Join(", ", parts) + "]";
        }
        if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
        return v.ToString() ?? "";
    }
}


