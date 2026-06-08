using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MCUSerialBridgeCLR;

namespace CoralinkerSDK;

internal sealed class SimulatedMcuNode : IRuntimeNode
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SimResponse>> _pending = new();
    private readonly Dictionary<byte, WireTapFlags> _wireTapFlags = new();
    private readonly Dictionary<byte, Action<byte, byte, byte[], uint>> _serialCallbacks = new();
    private readonly Dictionary<byte, Action<byte, byte, CANMessage, uint>> _canCallbacks = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private Process? _process;
    private Task? _readerTask;
    private bool _disposed;
    private bool _expectingProcessExit;
    private int _fatalReported;
    private string? _lastConsoleMessage;
    private bool _configured;
    private bool _programmed;
    private uint _startTick;
    private RuntimeStats _stats;

    public SimulatedMcuNode(string nodeId, string mcuUri)
    {
        NodeId = nodeId;
        McuUri = mcuUri;
        Version = CreateVersionInfo();
        Layout = CreateLayoutInfo();
        Abi = CreateAbiInfo();
        PortConfigs = CreateDefaultPortConfigs();
        State = CreateState(MCURunState.Idle, configured: false, programmed: false);
        Stats = CreateStats();
    }

    public string NodeId { get; }
    public string McuUri { get; }
    public bool IsConnected => _process is { HasExited: false };
    public bool IsRunning { get; private set; }
    public VersionInfo? Version { get; private set; }
    public LayoutInfo? Layout { get; private set; }
    public AbiInfo? Abi { get; private set; }
    public MCUState? State { get; private set; }
    public RuntimeStats? Stats { get; private set; }
    public string? LastError { get; private set; }
    public byte[] ProgramBytes { get; set; } = Array.Empty<byte>();
    public PortConfig[] PortConfigs { get; set; } = Array.Empty<PortConfig>();
    public CartFieldInfo[] CartFields { get; set; } = Array.Empty<CartFieldInfo>();

    public event Action<byte[]>? OnLowerIOReceived;
    public event Action<string, uint>? OnConsoleOutput;
    public event Action<ErrorPayload>? OnFatalError;
    public event Action<string>? OnError;

    public static VersionInfo CreateVersionInfo()
    {
        return new VersionInfo
        {
            ProductionName = "DIVER-SIM",
            GitTag = "sim",
            GitCommit = "local",
            BuildTime = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    /// <summary>
    /// 模拟节点上报的 DIVER 运行时 ABI（与 MCURuntime/mcu_runtime.h 同步）。
    /// </summary>
    public static AbiInfo CreateAbiInfo()
    {
        return new AbiInfo
        {
            Magic = AbiInfo.DiverMagic,
            AbiVersion = AbiInfo.CurrentAbiVersion
        };
    }

    public static LayoutInfo CreateLayoutInfo()
    {
        var ports = Enumerable.Range(0, 16)
            .Select(_ => new PortDescriptor { Type = PortType.Serial, Name = "" })
            .ToArray();
        ports[0] = new PortDescriptor { Type = PortType.Serial, Name = "RS485-A" };
        ports[1] = new PortDescriptor { Type = PortType.Serial, Name = "RS485-B" };
        ports[2] = new PortDescriptor { Type = PortType.Serial, Name = "RS232" };
        ports[3] = new PortDescriptor { Type = PortType.CAN, Name = "CAN" };

        return new LayoutInfo
        {
            DigitalInputCount = 32,
            DigitalOutputCount = 32,
            PortCount = 4,
            Reserved = 0,
            Ports = ports
        };
    }

    public static PortConfig[] CreateDefaultPortConfigs()
    {
        return new PortConfig[]
        {
            new SerialPortConfig(115200, 0),
            new SerialPortConfig(115200, 0),
            new SerialPortConfig(115200, 0),
            new CANPortConfig(1000000, 10)
        };
    }

    public bool Connect()
    {
        lock (_gate)
        {
            if (IsConnected)
            {
                return true;
            }

            var target = ResolveHostPath();
            _expectingProcessExit = false;
            Interlocked.Exchange(ref _fatalReported, 0);
            _lastConsoleMessage = null;
            var psi = new ProcessStartInfo
            {
                FileName = target.UseDotNet ? "dotnet" : target.ExecutablePath,
                Arguments = target.UseDotNet ? Quote(target.ExecutablePath) : "",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _process = Process.Start(psi);
            if (_process == null)
            {
                LastError = "Failed to start simulated node host.";
                OnError?.Invoke(LastError);
                return false;
            }

            _readerTask = Task.Run(() => ReadLoopAsync(_cts.Token));
            _ = Task.Run(() => ReadErrorLoopAsync(_process, _cts.Token));
            _ = Task.Run(() => MonitorProcessExitAsync(_process, _cts.Token));

            var ok = SendCommand("hello", new { nodeId = NodeId, mcuUri = McuUri }).Ok;
            if (!ok)
            {
                Disconnect();
                return false;
            }

            State = CreateState(MCURunState.Idle, _configured, _programmed);
            return true;
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            try
            {
                if (IsConnected)
                {
                    _expectingProcessExit = true;
                    _ = TrySendCommand("shutdown", null);
                }
            }
            catch
            {
                // Best effort during shutdown.
            }

            try
            {
                if (_process is { HasExited: false })
                {
                    _expectingProcessExit = true;
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process may already have exited.
            }

            _process?.Dispose();
            _process = null;
            IsRunning = false;
            State = CreateState(MCURunState.Idle, _configured, _programmed);
            Interlocked.Exchange(ref _fatalReported, 0);
        }
    }

    public bool Configure()
    {
        if (!EnsureConnected())
        {
            return false;
        }

        var response = SendCommand("configure", new
        {
            ports = PortConfigs.Select(p => Convert.ToBase64String(p.ToBytes())).ToArray()
        });
        _configured = response.Ok;
        State = CreateState(MCURunState.Idle, _configured, _programmed);
        return response.Ok;
    }

    public bool Program()
    {
        if (!EnsureConnected())
        {
            return false;
        }

        var response = SendCommand("program", new
        {
            program = Convert.ToBase64String(ProgramBytes),
            memorySize = Math.Max(1024 * 1024, ProgramBytes.Length + 256 * 1024)
        });
        _programmed = response.Ok;
        State = CreateState(MCURunState.Idle, _configured, _programmed);
        return response.Ok;
    }

    public bool Start()
    {
        if (!EnsureConnected())
        {
            return false;
        }

        var response = SendCommand("start", null);
        IsRunning = response.Ok;
        _startTick = EnvironmentTickMs();
        State = CreateState(response.Ok ? MCURunState.Running : MCURunState.Error, _configured, _programmed);
        return response.Ok;
    }

    public bool Stop()
    {
        if (!IsConnected)
        {
            IsRunning = false;
            return true;
        }

        var response = TrySendCommand("stop", null);
        IsRunning = false;
        State = CreateState(MCURunState.Idle, _configured, _programmed);
        return response?.Ok ?? true;
    }

    public bool SendUpperIO(byte[] data, uint timeoutMs = 20)
    {
        if (!IsConnected)
        {
            return false;
        }

        var response = TrySendCommand("upper", new { data = Convert.ToBase64String(data) });
        return response?.Ok ?? false;
    }

    public bool SetWireTap(byte portIndex, WireTapFlags flags, uint timeoutMs = 200)
    {
        if (portIndex == 0xFF)
        {
            for (byte i = 0; i < 16; i++)
            {
                _wireTapFlags[i] = flags;
            }
        }
        else
        {
            _wireTapFlags[portIndex] = flags;
        }
        var response = TrySendCommand("wiretap", new { portIndex, flags = (byte)flags });
        return response?.Ok ?? true;
    }

    public bool RegisterSerialPortCallback(byte portIndex, Action<byte, byte, byte[], uint> callback)
    {
        _serialCallbacks[portIndex] = callback;
        return true;
    }

    public bool RegisterCANPortCallback(byte portIndex, Action<byte, byte, CANMessage, uint> callback)
    {
        _canCallbacks[portIndex] = callback;
        return true;
    }

    public void RefreshState()
    {
        if (!IsConnected)
        {
            State = CreateState(MCURunState.Idle, _configured, _programmed);
        }
    }

    public void RefreshStats()
    {
        _stats.UptimeMs = IsRunning ? EnvironmentTickMs() - _startTick : _stats.UptimeMs;
        Stats = _stats;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        Disconnect();
        _cts.Dispose();
    }

    private bool EnsureConnected()
    {
        return IsConnected || Connect();
    }

    private SimResponse? TrySendCommand(string command, object? payload)
    {
        try
        {
            return SendCommand(command, payload);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            OnError?.Invoke(ex.Message);
            return null;
        }
    }

    private SimResponse SendCommand(string command, object? payload)
    {
        if (_process?.StandardInput == null || _process.HasExited)
        {
            throw new InvalidOperationException("Simulated node host is not running.");
        }

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<SimResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, tcs))
        {
            throw new InvalidOperationException("Failed to register simulated node command.");
        }

        var request = JsonSerializer.Serialize(new SimRequest(id, command, payload), JsonOptions);
        _process.StandardInput.WriteLine(request);
        _process.StandardInput.Flush();

        if (!tcs.Task.Wait(TimeSpan.FromSeconds(5)))
        {
            _pending.TryRemove(id, out _);
            throw new TimeoutException($"Simulated node command timed out: {command}");
        }

        var response = tcs.Task.GetAwaiter().GetResult();
        if (!response.Ok)
        {
            LastError = response.Error;
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                OnError?.Invoke(response.Error);
            }
        }
        return response;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process?.StandardOutput != null)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                HandleLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LastError = ex.Message;
            OnError?.Invoke(ex.Message);
        }
    }

    private async Task ReadErrorLoopAsync(Process process, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }
                OnConsoleOutput?.Invoke($"[simnode] {line}", 0);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task MonitorProcessExitAsync(Process process, CancellationToken ct)
    {
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            OnError?.Invoke(ex.Message);
            return;
        }

        if (ct.IsCancellationRequested || _disposed || _expectingProcessExit)
        {
            return;
        }

        IsRunning = false;
        State = CreateState(MCURunState.Error, _configured, _programmed);

        if (Volatile.Read(ref _fatalReported) == 1)
        {
            return;
        }

        var exitCode = TryGetExitCode(process);
        var message = exitCode == 0
            ? "Simulated node host exited unexpectedly."
            : $"Simulated node host exited unexpectedly with code {exitCode}.";
        if (!string.IsNullOrWhiteSpace(_lastConsoleMessage))
        {
            message += $" Last runtime message: {_lastConsoleMessage}";
        }

        LastError = message;
        OnError?.Invoke(message);
        ReportFatalError(message, ilOffset: -1, lineNo: 0);
    }

    private void HandleLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (root.TryGetProperty("id", out var idElement)
            && _pending.TryRemove(idElement.GetString() ?? "", out var tcs))
        {
            var ok = root.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : null;
            tcs.TrySetResult(new SimResponse(ok, error));
            return;
        }

        if (!root.TryGetProperty("event", out var eventElement))
        {
            return;
        }

        switch (eventElement.GetString())
        {
            case "lower":
                if (root.TryGetProperty("data", out var lowerData))
                {
                    var bytes = Convert.FromBase64String(lowerData.GetString() ?? "");
                    OnLowerIOReceived?.Invoke(bytes);
                }
                break;
            case "console":
                var consoleMessage = root.GetProperty("message").GetString() ?? "";
                _lastConsoleMessage = consoleMessage;
                OnConsoleOutput?.Invoke(
                    consoleMessage,
                    root.TryGetProperty("mcuTimestampMs", out var ts) ? ts.GetUInt32() : 0
                );
                break;
            case "snapshot":
                HandleSnapshotEvent(root);
                break;
            case "fatal":
                HandleFatalEvent(root);
                break;
            case "error":
                LastError = root.GetProperty("message").GetString();
                OnError?.Invoke(LastError ?? "Simulated node error");
                break;
            case "wire":
                HandleWireEvent(root);
                break;
        }
    }

    private void HandleWireEvent(JsonElement root)
    {
        var portIndex = root.GetProperty("portIndex").GetByte();
        var direction = root.GetProperty("direction").GetByte();
        var data = Convert.FromBase64String(root.GetProperty("data").GetString() ?? "");
        var timestamp = root.TryGetProperty("mcuTimestampMs", out var ts) ? ts.GetUInt32() : 0;

        UpdatePortStats(portIndex, direction, data.Length);
        if (!ShouldEmitWireTap(portIndex, direction))
        {
            return;
        }

        if (portIndex == 3)
        {
            var can = CANMessage.FromBytes(data, (uint)data.Length);
            if (can != null && _canCallbacks.TryGetValue(portIndex, out var canCallback))
            {
                canCallback(portIndex, direction, can, timestamp);
            }
        }
        else if (_serialCallbacks.TryGetValue(portIndex, out var serialCallback))
        {
            serialCallback(portIndex, direction, data, timestamp);
        }
    }

    private void HandleSnapshotEvent(JsonElement root)
    {
        var data = Convert.FromBase64String(root.GetProperty("data").GetString() ?? "");
        var bits = ReadUInt32LittleEndian(data);
        _stats.DigitalOutputs = bits;
        _stats.DigitalInputs = bits;
        Stats = _stats;
    }

    private void HandleFatalEvent(JsonElement root)
    {
        var message = root.GetProperty("message").GetString() ?? "MCU runtime fatal error";
        _lastConsoleMessage = message;
        IsRunning = false;
        State = CreateState(MCURunState.Error, _configured, _programmed);
        LastError = message;
        ReportFatalError(
            message,
            root.TryGetProperty("ilOffset", out var ilOffset) ? ilOffset.GetInt32() : -1,
            root.TryGetProperty("lineNo", out var lineNo) ? lineNo.GetInt32() : 0
        );
    }

    private void UpdatePortStats(byte portIndex, byte direction, int byteCount)
    {
        if (_stats.Ports == null || portIndex >= _stats.Ports.Length)
        {
            return;
        }

        var portStats = _stats.Ports[portIndex];
        if (direction == 1)
        {
            portStats.TxFrames++;
            portStats.TxBytes += (uint)Math.Max(0, byteCount);
        }
        else
        {
            portStats.RxFrames++;
            portStats.RxBytes += (uint)Math.Max(0, byteCount);
        }
        _stats.Ports[portIndex] = portStats;
        Stats = _stats;
    }

    private bool ShouldEmitWireTap(byte portIndex, byte direction)
    {
        if (!_wireTapFlags.TryGetValue(portIndex, out var flags))
        {
            _wireTapFlags.TryGetValue(0xFF, out flags);
        }

        if (flags == WireTapFlags.None)
        {
            return false;
        }

        return direction == 1
            ? flags.HasFlag(WireTapFlags.TX)
            : flags.HasFlag(WireTapFlags.RX);
    }

    private static uint ReadUInt32LittleEndian(byte[] data)
    {
        if (data.Length < 4)
        {
            var padded = new byte[4];
            Buffer.BlockCopy(data, 0, padded, 0, data.Length);
            return BitConverter.ToUInt32(padded, 0);
        }
        return BitConverter.ToUInt32(data, 0);
    }

    private static MCUState CreateState(MCURunState runningState, bool configured, bool programmed)
    {
        return new MCUState
        {
            RunningState = runningState,
            IsConfigured = configured ? (byte)1 : (byte)0,
            IsProgrammed = programmed ? (byte)1 : (byte)0,
            Mode = MCUMode.DIVER
        };
    }

    private void ReportFatalError(string message, int ilOffset, int lineNo)
    {
        if (Interlocked.Exchange(ref _fatalReported, 1) == 1)
        {
            return;
        }

        OnFatalError?.Invoke(CreateStringErrorPayload(message, ilOffset, lineNo));
    }

    private static ErrorPayload CreateStringErrorPayload(string message, int ilOffset, int lineNo)
    {
        var raw = new byte[128];
        var bytes = Encoding.UTF8.GetBytes(message);
        Buffer.BlockCopy(bytes, 0, raw, 0, Math.Min(bytes.Length, raw.Length - 1));

        return new ErrorPayload
        {
            PayloadVersion = 1,
            Version = CreateVersionInfo(),
            DebugInfo = new DIVERDebugInfo
            {
                ILOffset = ilOffset,
                LineNo = lineNo,
                Reserved = new uint[14]
            },
            CoreDumpLayoutValue = (uint)CoreDumpLayout.String,
            CoreDump = new CoreDumpData
            {
                Raw = raw
            }
        };
    }

    private static RuntimeStats CreateStats()
    {
        return new RuntimeStats
        {
            UptimeMs = 0,
            DigitalInputs = 0,
            DigitalOutputs = 0,
            PortCount = 4,
            Reserved = new byte[3],
            Ports = Enumerable.Range(0, 16).Select(_ => new PortStats()).ToArray()
        };
    }

    private static uint EnvironmentTickMs()
    {
        return unchecked((uint)Environment.TickCount);
    }

    private static int TryGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return int.MinValue;
        }
    }

    private static SimHostTarget ResolveHostPath()
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "CoralinkerSimNodeHost.exe"
            : "CoralinkerSimNodeHost";
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "simnode", fileName),
            Path.Combine(baseDir, fileName),
            Path.Combine(baseDir, "simnode", "CoralinkerSimNodeHost.dll"),
            Path.Combine(baseDir, "CoralinkerSimNodeHost.dll"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CoralinkerSimNodeHost", "bin", "Debug", "net8.0", "CoralinkerSimNodeHost.dll")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CoralinkerSimNodeHost", "bin", "Release", "net8.0", "CoralinkerSimNodeHost.dll")),
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }
            return new SimHostTarget(candidate, candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        }

        throw new FileNotFoundException(
            "Cannot locate CoralinkerSimNodeHost. Build or publish the Host so the simnode helper is available."
        );
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private sealed record SimHostTarget(string ExecutablePath, bool UseDotNet);
    private sealed record SimRequest(string Id, string Command, object? Payload);
    private sealed record SimResponse(bool Ok, string? Error);
}
