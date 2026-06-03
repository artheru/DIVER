using System.Reflection;
using System.Runtime.InteropServices;

namespace CoralinkerSimNodeHost;

internal static class NativeMcuRuntimeResolver
{
    private const string LibraryName = "sim_node_runtime";
    private static readonly object Gate = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(NativeMcuRuntimeResolver).Assembly, Resolve);
            _registered = true;
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in GetCandidatePaths())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static string[] GetCandidatePaths()
    {
        var baseDir = AppContext.BaseDirectory;
        var rid = GetRuntimeIdentifier();
        var fileName = GetNativeFileName();
        return new[]
        {
            Path.Combine(baseDir, "runtimes", rid, "native", fileName),
            Path.Combine(baseDir, "..", "runtimes", rid, "native", fileName),
            Path.Combine(baseDir, fileName),
        };
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "win-x64" : "win-x86";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        }

        throw new PlatformNotSupportedException(
            $"Unsupported mcu_runtime platform: {RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture}"
        );
    }

    private static string GetNativeFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "sim_node_runtime.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "libsim_node_runtime.so";
        }

        return LibraryName;
    }
}
