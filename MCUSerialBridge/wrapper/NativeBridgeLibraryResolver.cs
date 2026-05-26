using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class NativeBridgeLibraryResolver
{
    private const string LibraryName = "mcu_serial_bridge";
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

            try
            {
                NativeLibrary.SetDllImportResolver(
                    typeof(NativeBridgeLibraryResolver).Assembly,
                    Resolve
                );
            }
            catch (InvalidOperationException)
            {
                // Another entry point in this assembly registered the resolver first.
            }

            _registered = true;
        }
    }

    private static IntPtr Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath
    )
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in GetCandidatePaths())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (NativeLibrary.TryLoad(candidate, out var handle))
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
            Path.Combine(baseDir, fileName),
        };
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? "win-x64"
                : "win-x86";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "linux-arm64"
                : "linux-x64";
        }

        throw new PlatformNotSupportedException(
            $"Unsupported native bridge platform: {RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture}"
        );
    }

    private static string GetNativeFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "mcu_serial_bridge.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "libmcu_serial_bridge.so";
        }

        return LibraryName;
    }
}
