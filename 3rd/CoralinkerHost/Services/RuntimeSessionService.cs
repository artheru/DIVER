using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using CoralinkerSDK;

namespace CoralinkerHost.Services;

public sealed class RuntimeSessionService
{
    private readonly DiverBuildService _builder;
    private readonly TerminalBroadcaster _terminal;

    private readonly object _gate = new();
    private RuntimeSession? _session;

    public RuntimeSessionService(DiverBuildService builder, TerminalBroadcaster terminal)
    {
        _builder = builder;
        _terminal = terminal;
    }

    public RuntimeSessionSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_session == null) return new RuntimeSessionSnapshot(false, null, null);
            return new RuntimeSessionSnapshot(true, _session.AssetName, _session.Build?.BuildRoot);
        }
    }

    public object? GetCartTarget()
    {
        lock (_gate) return _session?.CartTarget;
    }

    public async Task<RuntimeSessionSnapshot> RunAsync(ProjectState project, string logicFileName, string logicCs, CancellationToken ct)
    {
        await _terminal.LineAsync($"[run] Building from asset: {logicFileName}", ct);
        var build = await _builder.BuildFromLogicCsAsync(logicFileName, logicCs, ct);

        if (build.Artifacts.Count == 0)
            throw new InvalidOperationException("Build produced no *.bin artifacts. Ensure your logic class has [LogicRunOnMCU].");

        var cart = CreateCartTarget(build);
        await _terminal.LineAsync($"[run] Cart target: {cart.GetType().FullName}", ct);

        var peam = new PEAMInterface(cart);

        var configs = project.Nodes
            .Where(n => !string.Equals(n.Kind, "root", StringComparison.OrdinalIgnoreCase))
            .Select(n =>
            {
                var mcuUri = n.Properties.TryGetValue("mcuUri", out var u) ? u : "";
                var logicName = n.Properties.TryGetValue("logicName", out var ln) ? ln : "";
                if (string.IsNullOrWhiteSpace(mcuUri)) throw new InvalidOperationException($"Node {n.Id} has empty mcuUri.");
                if (string.IsNullOrWhiteSpace(logicName)) throw new InvalidOperationException($"Node {n.Id} has empty logicName.");
                if (!build.Artifacts.TryGetValue(logicName, out var art))
                    throw new InvalidOperationException($"No build artifacts found for logicName '{logicName}'. Produced: {string.Join(", ", build.Artifacts.Keys)}");

                return new PEAMInterface.NodeConfiguration
                {
                    asmBytes = File.ReadAllBytes(art.BinPath),
                    metaJson = File.ReadAllText(art.MetaJsonPath),
                    diverSrc = File.ReadAllText(art.DiverPath),
                    diverMapJson = File.ReadAllText(art.DiverMapPath),
                    mcuUri = mcuUri,
                    name = logicName
                };
            })
            .ToArray();

        if (configs.Length == 0)
        {
            await _terminal.LineAsync("[run] No MCU nodes in graph. Starting LocalDebugDIVERVehicle.RunDIVER() (PC simulation)...", ct);
            TryLoadNativeRuntime(build);
            InvokeRunDiver(cart);
        }
        else
        {
            await _terminal.LineAsync($"[run] Starting {configs.Length} node(s) via PEAMInterface...", ct);
            peam.RunDIVER(configs);
        }

        lock (_gate)
        {
            _session?.Dispose();
            _session = new RuntimeSession(logicFileName, build, cart, peam);
        }

        return GetSnapshot();
    }

    public Task StopAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
        }

        return _terminal.LineAsync("[run] Stopped. (PEAMInterface.StopDIVER is pending)", ct);
    }

    private static object CreateCartTarget(BuildResult build)
    {
        if (string.IsNullOrWhiteSpace(build.OutputDllPath) || !File.Exists(build.OutputDllPath))
            throw new InvalidOperationException("Could not locate DiverTest.dll in build output.");

        var dir = Path.GetDirectoryName(build.OutputDllPath)!;
        var alc = new IsolatedLoadContext(dir);
        var asm = alc.LoadFromAssemblyPath(build.OutputDllPath);

        // Heuristic: pick the first type named "*Vehicle" or first public, non-abstract class.
        var type =
            asm.GetTypes().FirstOrDefault(t => t is { IsAbstract: false, IsClass: true } && t.Name.EndsWith("Vehicle", StringComparison.OrdinalIgnoreCase))
            ?? asm.GetTypes().First(t => t is { IsAbstract: false, IsClass: true } && t.GetConstructor(Type.EmptyTypes) != null);

        return Activator.CreateInstance(type)!;
    }

    private static void InvokeRunDiver(object cart)
    {
        var m = cart.GetType().GetMethod("RunDIVER", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null)
            throw new InvalidOperationException($"Cart object type {cart.GetType().FullName} does not provide RunDIVER().");
        m.Invoke(cart, null);
    }

    private static void TryLoadNativeRuntime(BuildResult build)
    {
        // Local debug P/Invokes "MCURuntime.dll". Ensure the compiled native DLL is loadable.
        if (string.IsNullOrWhiteSpace(build.OutputDllPath)) return;
        var dir = Path.GetDirectoryName(build.OutputDllPath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        var candidates = new[]
        {
            Path.Combine(dir, "MCURuntime.dll"),
            Path.Combine(dir, "mcuruntime.dll"),
        };

        foreach (var c in candidates)
        {
            if (!File.Exists(c)) continue;
            try
            {
                NativeLibrary.Load(c);
                return;
            }
            catch
            {
                // keep trying
            }
        }
    }

    private sealed class RuntimeSession : IDisposable
    {
        public string AssetName { get; }
        public BuildResult? Build { get; }
        public object CartTarget { get; }
        public PEAMInterface Peam { get; }

        public RuntimeSession(string assetName, BuildResult build, object cartTarget, PEAMInterface peam)
        {
            AssetName = assetName;
            Build = build;
            CartTarget = cartTarget;
            Peam = peam;
        }

        public void Dispose()
        {
            // NOTE: Build folders are left on disk for now to aid debugging.
            // TODO: implement PEAM stop + unloading collectible ALC.
        }
    }

    private sealed class IsolatedLoadContext : AssemblyLoadContext
    {
        private readonly string _dir;

        public IsolatedLoadContext(string dir) : base(isCollectible: true)
        {
            _dir = dir;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var candidate = Path.Combine(_dir, $"{assemblyName.Name}.dll");
            if (File.Exists(candidate))
                return LoadFromAssemblyPath(candidate);
            return null;
        }
    }
}

public sealed record RuntimeSessionSnapshot(bool IsRunning, string? AssetName, string? BuildRoot);


