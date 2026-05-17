using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoralinkerSDK;

namespace CoralinkerHost.Services;

public sealed class RootRuntimeService : IDisposable
{
    private const string RootVirtualNodeId = "root-runtime";
    private static readonly object ConsoleCaptureGate = new();
    private readonly ProjectStore _store;
    private readonly TerminalBroadcaster _terminal;
    private readonly object _gate = new();

    private AssemblyLoadContext? _alc;
    private object? _logic;
    private object? _cart;
    private RootLogicMetadata? _metadata;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private string _statusText = "/";
    private int _iteration;

    public RootRuntimeService(ProjectStore store, TerminalBroadcaster terminal)
    {
        _store = store;
        _terminal = terminal;
    }

    public bool IsRunning => _loopTask is { IsCompleted: false };
    public string? CurrentLogicName => _metadata?.Name;
    public string StatusText => _statusText;

    public IReadOnlyList<RootLogicMetadata> ListRootLogics()
    {
        _store.EnsureDataLayout();
        if (!Directory.Exists(_store.GeneratedDir)) return Array.Empty<RootLogicMetadata>();

        var result = new List<RootLogicMetadata>();
        foreach (var file in Directory.GetFiles(_store.GeneratedDir, "*.root.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<RootLogicMetadata>(
                    File.ReadAllText(file),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (meta != null) result.Add(meta);
            }
            catch
            {
                // Ignore malformed stale generated files.
            }
        }
        return result.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public RootRuntimeState GetState()
    {
        lock (_gate)
        {
            var configured = _metadata?.Name ?? _store.Get().RootLogicName;
            var meta = _metadata ?? RegisterConfiguredRootLocked();
            return new RootRuntimeState(
                IsRunning,
                configured,
                meta?.SourceCommit,
                meta?.SourceCommitShort,
                meta?.SourceCommitTime,
                meta?.BuildTime,
                meta?.BuildId,
                _statusText,
                meta?.CartFields ?? Array.Empty<RootFieldMetadata>(),
                meta?.ControlFields ?? Array.Empty<RootFieldMetadata>());
        }
    }

    public void Configure(string? logicName)
    {
        lock (_gate)
        {
            if (IsRunning) throw new InvalidOperationException("Stop Root runtime before changing Root logic.");
            var state = _store.Get() with { RootLogicName = logicName };
            _store.Set(state);
            _store.SaveToDisk();
            RegisterConfiguredRootLocked();
        }
    }

    public void SetControl(string name, object? value)
    {
        lock (_gate)
        {
            RegisterConfiguredRootLocked();
            if (!DIVERSession.Instance.SetVirtualControlField(name, CoerceJsonValue(value)))
            {
                throw new InvalidOperationException($"Root control field not found: {name}");
            }
        }
    }

    public void EnsureConfiguredRegistered()
    {
        lock (_gate)
        {
            RegisterConfiguredRootLocked();
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        RootLogicMetadata? meta = null;
        var skipped = false;
        lock (_gate)
        {
            if (IsRunning) return;
            var logicName = _store.Get().RootLogicName;
            if (string.IsNullOrWhiteSpace(logicName))
            {
                DIVERSession.Instance.UnregisterVirtualNode(RootVirtualNodeId);
                skipped = true;
            }
            else
            {
                meta = ListRootLogics().FirstOrDefault(x => string.Equals(x.Name, logicName, StringComparison.OrdinalIgnoreCase));
                if (meta == null) throw new InvalidOperationException($"Root logic not found: {logicName}");
                RegisterRootVirtualNode(meta);
                LoadLogicLocked(meta);
                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
            }
        }

        if (skipped)
        {
            await _terminal.LineAsync("[root] No Root logic configured; skipped", ct);
            return;
        }

        if (meta == null) return;
        await _terminal.LineAsync($"[root] Started {meta.Name} ({meta.SourceCommitShort}, build={meta.BuildTime:yyyy-MM-dd HH:mm:ss})", ct);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_gate)
        {
            cts = _cts;
            task = _loopTask;
            _cts = null;
            _loopTask = null;
        }

        if (cts != null)
        {
            cts.Cancel();
            try
            {
                if (task != null) await task.WaitAsync(TimeSpan.FromSeconds(2), ct);
            }
            catch { }
            cts.Dispose();
        }

        lock (_gate)
        {
            _logic = null;
            _cart = null;
            _metadata = null;
            _alc?.Unload();
            _alc = null;
        }
        await _terminal.LineAsync("[root] Stopped", ct);
    }

    private void LoadLogicLocked(RootLogicMetadata meta)
    {
        _alc?.Unload();
        _alc = new AssemblyLoadContext("RootRuntime-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        var asm = _alc.LoadFromAssemblyPath(meta.AssemblyPath);
        var type = asm.GetType(meta.TypeName) ?? throw new InvalidOperationException($"Root type not found: {meta.TypeName}");
        _logic = Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Cannot create Root logic: {meta.TypeName}");

        var cartType = ResolveRootCartType(type) ?? throw new InvalidOperationException($"Cannot resolve cart type for {meta.TypeName}");
        _cart = Activator.CreateInstance(cartType) ?? throw new InvalidOperationException($"Cannot create cart: {cartType.FullName}");
        type.GetField("cart")?.SetValue(_logic, _cart);
        _metadata = meta;
        _iteration = 0;
        _statusText = "/";
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            RootLogicMetadata? meta;
            object? logic;
            object? cart;
            lock (_gate)
            {
                meta = _metadata;
                logic = _logic;
                cart = _cart;
            }
            if (meta == null || logic == null || cart == null) break;

            try
            {
                ApplyVariablesToCart(cart, meta);
                ApplyControlsToLogic(logic, meta);

                logic.GetType().GetField("interval")?.SetValue(logic, (float)meta.ScanInterval);
                InvokeOperationWithConsoleCapture(logic);
                _iteration++;

                var status = logic.GetType().GetField("statusText")?.GetValue(logic) as string;
                if (status != null) _statusText = status;
                PublishUpperFields(cart, meta);
            }
            catch (Exception ex)
            {
                await _terminal.LineAsync($"[root] ERROR: {ex.GetBaseException().Message}", CancellationToken.None);
            }

            try
            {
                await Task.Delay(Math.Max(1, meta.ScanInterval), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ApplyVariablesToCart(object cart, RootLogicMetadata meta)
    {
        var cartType = cart.GetType();
        foreach (var field in meta.CartFields)
        {
            var fi = cartType.GetField(field.Name);
            if (fi == null) continue;
            var value = DIVERSession.Instance.GetCartField(field.Name);
            if (value != null)
            {
                fi.SetValue(cart, ConvertValue(value, fi.FieldType));
            }
        }
    }

    private void ApplyControlsToLogic(object logic, RootLogicMetadata meta)
    {
        var type = logic.GetType();
        foreach (var field in meta.ControlFields)
        {
            var fi = type.GetField(field.Name);
            if (fi == null) continue;
            var value = DIVERSession.Instance.GetCartField(field.Name);
            if (value != null)
            {
                fi.SetValue(logic, ConvertValue(value, fi.FieldType));
            }
        }
    }

    private RootLogicMetadata? RegisterConfiguredRootLocked()
    {
        var logicName = _store.Get().RootLogicName;
        if (string.IsNullOrWhiteSpace(logicName))
        {
            DIVERSession.Instance.UnregisterVirtualNode(RootVirtualNodeId);
            return null;
        }

        var meta = ListRootLogics().FirstOrDefault(x => string.Equals(x.Name, logicName, StringComparison.OrdinalIgnoreCase));
        if (meta == null)
        {
            DIVERSession.Instance.UnregisterVirtualNode(RootVirtualNodeId);
            return null;
        }

        RegisterRootVirtualNode(meta);
        return meta;
    }

    private static void RegisterRootVirtualNode(RootLogicMetadata meta)
    {
        DIVERSession.Instance.RegisterVirtualNode(
            RootVirtualNodeId,
            $"Root:{meta.Name}",
            meta.CartFields.Select(f => new VirtualCartFieldDeclaration(
                f.Name,
                f.TypeId,
                IsLowerIO: f.Direction == "lower",
                IsUpperIO: f.Direction == "upper",
                IsMutual: f.Direction == "mutual",
                IsControl: false,
                IsRootCart: true)),
            meta.ControlFields.Select(f => new VirtualCartFieldDeclaration(
                f.Name,
                f.TypeId,
                IsLowerIO: false,
                IsUpperIO: false,
                IsMutual: false,
                IsControl: true,
                IsRootCart: false)));
    }

    private void PublishUpperFields(object cart, RootLogicMetadata meta)
    {
        var cartType = cart.GetType();
        foreach (var field in meta.CartFields.Where(f => f.Direction == "upper"))
        {
            var fi = cartType.GetField(field.Name);
            if (fi == null) continue;
            var value = fi.GetValue(cart);
            if (value != null)
            {
                if (!DIVERSession.Instance.SetCartFieldAndSignalUpperIO(field.Name, value))
                {
                    _ = _terminal.LineAsync($"[root] Failed to publish UpperIO field: {field.Name}", CancellationToken.None);
                }
            }
        }
    }

    private void InvokeOperationWithConsoleCapture(object logic)
    {
        lock (ConsoleCaptureGate)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var capture = new RootConsoleWriter(originalOut, line => _ = _terminal.RootLineAsync(line, CancellationToken.None));
            using var errorCapture = new RootConsoleWriter(originalError, line => _ = _terminal.RootLineAsync("[stderr] " + line, CancellationToken.None));

            try
            {
                Console.SetOut(capture);
                Console.SetError(errorCapture);
                logic.GetType().GetMethod("Operation")?.Invoke(logic, null);
            }
            finally
            {
                capture.Flush();
                errorCapture.Flush();
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }

    private static Type? ResolveRootCartType(Type type)
    {
        var current = type;
        while (current != null)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition().FullName?.StartsWith("CartActivator.RootLogic`1", StringComparison.Ordinal) == true)
            {
                return current.GetGenericArguments()[0];
            }
            current = current.BaseType;
        }
        return null;
    }

    private static object CoerceJsonValue(object? value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => je.TryGetInt32(out var i) ? i : je.GetSingle(),
                JsonValueKind.String => je.GetString() ?? "",
                _ => ""
            };
        }
        return value ?? 0;
    }

    private static object ConvertValue(object value, Type target)
    {
        value = CoerceJsonValue(value);
        if (target == typeof(bool)) return Convert.ToBoolean(value);
        if (target == typeof(byte)) return Convert.ToByte(value);
        if (target == typeof(sbyte)) return Convert.ToSByte(value);
        if (target == typeof(short)) return Convert.ToInt16(value);
        if (target == typeof(ushort)) return Convert.ToUInt16(value);
        if (target == typeof(int)) return Convert.ToInt32(value);
        if (target == typeof(uint)) return Convert.ToUInt32(value);
        if (target == typeof(float)) return Convert.ToSingle(value);
        return value;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _alc?.Unload();
    }
}

internal sealed class RootConsoleWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly Action<string> _onLine;
    private readonly object _gate = new();
    private readonly StringBuilder _line = new();

    public RootConsoleWriter(TextWriter inner, Action<string> onLine)
    {
        _inner = inner;
        _onLine = onLine;
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        _inner.Write(value);
        lock (_gate)
        {
            if (value == '\n')
            {
                EmitLineLocked();
            }
            else if (value != '\r')
            {
                _line.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        foreach (var ch in value)
        {
            Write(ch);
        }
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        Write('\n');
    }

    public override void Flush()
    {
        _inner.Flush();
        lock (_gate)
        {
            if (_line.Length > 0)
            {
                EmitLineLocked();
            }
        }
    }

    private void EmitLineLocked()
    {
        var text = _line.ToString();
        _line.Clear();
        if (!string.IsNullOrWhiteSpace(text))
        {
            _onLine(text);
        }
    }
}

public sealed record RootRuntimeState(
    bool IsRunning,
    string? LogicName,
    string? SourceCommit,
    string? SourceCommitShort,
    DateTimeOffset? SourceCommitTime,
    DateTimeOffset? BuildTime,
    string? BuildId,
    string StatusText,
    RootFieldMetadata[] CartFields,
    RootFieldMetadata[] ControlFields);
