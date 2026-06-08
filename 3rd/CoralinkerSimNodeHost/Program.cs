using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CoralinkerSimNodeHost;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object WriteGate = new();
    private static CancellationTokenSource? _runCts;
    private static Task? _runTask;
    private static int _scanIntervalMs = 100;

    private static readonly McuRuntimeNative.BytesCallback LowerCallback = OnLower;
    private static readonly McuRuntimeNative.TextCallback ConsoleCallback = OnConsole;
    private static readonly McuRuntimeNative.BytesCallback SnapshotCallback = OnSnapshot;
    private static readonly McuRuntimeNative.PortBytesCallback StreamCallback = OnPortBytes;
    private static readonly McuRuntimeNative.PortBytesCallback EventCallback = OnPortBytes;
    private static readonly McuRuntimeNative.FatalCallback FatalCallback = OnFatal;

    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        NativeMcuRuntimeResolver.EnsureRegistered();

        string? line;
        while ((line = await Console.In.ReadLineAsync()) != null)
        {
            try
            {
                HandleRequest(line);
            }
            catch (Exception ex)
            {
                WriteEvent("error", new { message = ex.Message });
            }
        }

        StopLoop();
        TryDestroyRuntime();
        return 0;
    }

    private static void HandleRequest(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetString() ?? "";
        var command = root.GetProperty("command").GetString() ?? "";
        var payload = root.TryGetProperty("payload", out var payloadElement) ? payloadElement : default;

        switch (command)
        {
            case "hello":
                WriteResponse(id, true);
                break;
            case "configure":
                WriteResponse(id, true);
                break;
            case "program":
                ProgramRuntime(id, payload);
                break;
            case "start":
                StartLoop();
                WriteResponse(id, true);
                break;
            case "stop":
                StopLoop();
                WriteResponse(id, true);
                break;
            case "upper":
                PutUpper(id, payload);
                break;
            case "wiretap":
                WriteResponse(id, true);
                break;
            case "shutdown":
                WriteResponse(id, true);
                StopLoop();
                Environment.Exit(0);
                break;
            default:
                WriteResponse(id, false, $"Unknown command: {command}");
                break;
        }
    }

    private static void ProgramRuntime(string id, JsonElement payload)
    {
        var program = Convert.FromBase64String(payload.GetProperty("program").GetString() ?? "");
        var memorySize = payload.TryGetProperty("memorySize", out var memoryElement)
            ? memoryElement.GetInt32()
            : Math.Max(1024 * 1024, program.Length + 256 * 1024);

        int interval;
        try
        {
            McuRuntimeNative.SetCallbacks(LowerCallback, ConsoleCallback, SnapshotCallback, StreamCallback, EventCallback, FatalCallback);
            interval = McuRuntimeNative.LoadProgram(program, program.Length, memorySize);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            WriteResponse(id, false, $"Cannot load mcu_runtime native library: {ex.Message}");
            return;
        }

        if (interval < 0)
        {
            WriteResponse(id, false, "mcu_runtime rejected the program.");
            return;
        }

        _scanIntervalMs = Math.Max(1, interval);
        WriteResponse(id, true);
    }

    private static void PutUpper(string id, JsonElement payload)
    {
        var data = Convert.FromBase64String(payload.GetProperty("data").GetString() ?? "");
        int result;
        try
        {
            result = McuRuntimeNative.PutUpper(data, data.Length);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            WriteResponse(id, false, $"Cannot load mcu_runtime native library: {ex.Message}");
            return;
        }
        WriteResponse(id, result == 0, result == 0 ? null : "Failed to put upper IO data.");
    }

    private static void StartLoop()
    {
        if (_runTask is { IsCompleted: false })
        {
            return;
        }

        _runCts = new CancellationTokenSource();
        _runTask = Task.Run(async () =>
        {
            var ct = _runCts.Token;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var nextDueMs = 0L;
            while (!ct.IsCancellationRequested)
            {
                var remainingMs = nextDueMs - stopwatch.ElapsedMilliseconds;
                if (remainingMs > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(remainingMs), ct).ConfigureAwait(false);
                }

                var timestampMs = unchecked((uint)Math.Max(0, nextDueMs));
                try
                {
                    McuRuntimeNative.Step(timestampMs);
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    WriteEvent("error", new { message = $"Cannot run mcu_runtime native library: {ex.Message}" });
                    break;
                }
                nextDueMs += _scanIntervalMs;
            }
        }, _runCts.Token);
    }

    private static void StopLoop()
    {
        try
        {
            _runCts?.Cancel();
            _runTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Best effort during process teardown.
        }
        finally
        {
            _runTask = null;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private static void OnLower(IntPtr data, int length, uint timestampMs)
    {
        WriteEvent("lower", new
        {
            data = ToBase64(data, length),
            mcuTimestampMs = timestampMs
        });
    }

    private static void OnConsole(IntPtr message, uint timestampMs)
    {
        WriteEvent("console", new
        {
            message = Marshal.PtrToStringUTF8(message) ?? "",
            mcuTimestampMs = timestampMs
        });
    }

    private static void OnSnapshot(IntPtr data, int length, uint timestampMs)
    {
        WriteEvent("snapshot", new
        {
            data = ToBase64(data, length),
            mcuTimestampMs = timestampMs
        });
    }

    private static void OnPortBytes(byte portIndex, byte direction, IntPtr data, int length, uint timestampMs)
    {
        OnWire(portIndex, direction, data, length, timestampMs);
        if (direction != 1)
        {
            return;
        }

        // Loopback model: TX becomes RX on the paired virtual interface.
        var rxPort = portIndex switch
        {
            0 => (byte)1,
            1 => (byte)0,
            _ => portIndex
        };
        OnWire(rxPort, 0, data, length, timestampMs);
        try
        {
            var loopbackData = ToBytes(data, length);
            McuRuntimeNative.PutPortInput(rxPort, loopbackData, loopbackData.Length);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            WriteEvent("error", new { message = $"Cannot loop back port input: {ex.Message}" });
        }
    }

    private static void OnFatal(int ilOffset, IntPtr message, int lineNo, uint timestampMs)
    {
        WriteEvent("fatal", new
        {
            message = Marshal.PtrToStringUTF8(message) ?? "MCU runtime fatal error",
            ilOffset,
            lineNo,
            mcuTimestampMs = timestampMs
        });
    }

    private static void OnWire(byte portIndex, byte direction, IntPtr data, int length, uint timestampMs)
    {
        WriteEvent("wire", new
        {
            portIndex,
            direction,
            data = ToBase64(data, length),
            mcuTimestampMs = timestampMs
        });
    }

    private static string ToBase64(IntPtr data, int length)
    {
        return Convert.ToBase64String(ToBytes(data, length));
    }

    private static byte[] ToBytes(IntPtr data, int length)
    {
        if (length <= 0 || data == IntPtr.Zero)
        {
            return Array.Empty<byte>();
        }

        var bytes = new byte[length];
        Marshal.Copy(data, bytes, 0, length);
        return bytes;
    }

    private static void WriteResponse(string id, bool ok, string? error = null)
    {
        WriteObject(new { id, ok, error });
    }

    private static void WriteEvent(string eventName, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var map = new Dictionary<string, object?> { ["event"] = eventName };
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            map[property.Name] = property.Value.Clone();
        }
        WriteObject(map);
    }

    private static void WriteObject(object value)
    {
        lock (WriteGate)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            Console.Out.Flush();
        }
    }

    private static void TryDestroyRuntime()
    {
        try
        {
            McuRuntimeNative.Destroy();
        }
        catch
        {
            // Native runtime may never have been loaded.
        }
    }
}
