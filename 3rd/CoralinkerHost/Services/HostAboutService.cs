using System.Reflection;
using System.Text.Json;
using MCUSerialBridgeCLR;

namespace CoralinkerHost.Services;

public sealed class HostAboutService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HostRuntimePaths _paths;
    private readonly IWebHostEnvironment _env;
    private HostAboutSnapshot? _cached;

    public HostAboutService(HostRuntimePaths paths, IWebHostEnvironment env)
    {
        _paths = paths;
        _env = env;
    }

    public HostAboutSnapshot GetAbout()
    {
        if (_cached != null)
        {
            return _cached;
        }

        var publishInfo = TryReadPublishInfo();
        var frontendInfo = TryReadFrontendBuildInfo();
        var assembly = typeof(Program).Assembly;
        var assemblyVersion = assembly.GetName().Version?.ToString() ?? "unknown";
        var assemblyBuildTime = GetAssemblyBuildTime(assembly);

        var backend = new HostVersionInfo(
            App: publishInfo?.App ?? "CoralinkerHost",
            Tag: publishInfo?.Tag ?? TryReadGitTag(),
            Commit: publishInfo?.Commit ?? TryReadGitCommit(),
            CommitTime: publishInfo?.CommitTime,
            BuildTime: publishInfo?.PublishTime ?? assemblyBuildTime,
            Configuration: publishInfo?.Configuration,
            Dirty: publishInfo?.Dirty,
            Version: assemblyVersion,
            Layout: _paths.RunLayout.ToString()
        );

        var frontend = frontendInfo ?? new HostVersionInfo(
            App: "CoralinkerHost UI",
            Tag: null,
            Commit: null,
            CommitTime: null,
            BuildTime: null,
            Configuration: _env.IsDevelopment() ? "development" : null,
            Dirty: null,
            Version: null,
            Layout: _paths.RunLayout.ToString()
        );

        _cached = new HostAboutSnapshot(backend, frontend, BuildDiverAbi());
        return _cached;
    }

    /// <summary>
    /// 本 Host 内置编译器 / 运行时所对应的 DIVER 程序二进制 ABI（SemVer X.Y.Z）。
    /// 取自 <see cref="AbiInfo.CurrentAbiVersion"/>，须与 MCURuntime/mcu_runtime.h 同步。
    /// </summary>
    private static DiverAbiInfo BuildDiverAbi()
    {
        var ver = AbiInfo.CurrentAbiVersion;
        int major = (int)((ver >> 16) & 0xFF);
        int minor = (int)((ver >> 8) & 0xFF);
        int patch = (int)(ver & 0xFF);
        return new DiverAbiInfo(
            Magic: AbiInfo.DiverMagic,
            AbiVersion: ver,
            Major: major,
            Minor: minor,
            Patch: patch,
            SemVer: $"{major}.{minor}.{patch}");
    }

    private PublishInfoDocument? TryReadPublishInfo()
    {
        var path = ResolvePublishInfoPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PublishInfoDocument>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string ResolvePublishInfoPath()
    {
        var contentRootPath = Path.Combine(_paths.ContentRoot, "publish-info.json");
        if (File.Exists(contentRootPath))
        {
            return contentRootPath;
        }

        var packageRootPath = Path.GetFullPath(Path.Combine(_paths.ContentRoot, "..", "publish-info.json"));
        return packageRootPath;
    }

    private HostVersionInfo? TryReadFrontendBuildInfo()
    {
        var path = Path.Combine(_paths.ContentRoot, "wwwroot", "build-info.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<FrontendBuildInfoDocument>(json, JsonOptions);
            if (doc == null)
            {
                return null;
            }

            return new HostVersionInfo(
                App: doc.App ?? "CoralinkerHost UI",
                Tag: doc.Tag,
                Commit: doc.Commit,
                CommitTime: doc.CommitTime,
                BuildTime: doc.BuildTime,
                Configuration: doc.Configuration,
                Dirty: doc.Dirty,
                Version: doc.Version,
                Layout: _paths.RunLayout.ToString()
            );
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? GetAssemblyBuildTime(Assembly assembly)
    {
        var path = assembly.Location;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        return File.GetLastWriteTimeUtc(path);
    }

    private string? TryReadGitCommit()
    {
        return TryRunGit(new[] { "rev-parse", "--short", "HEAD" });
    }

    private string? TryReadGitTag()
    {
        return TryRunGit(new[] { "describe", "--tags", "--always", "--dirty" });
    }

    private string? TryRunGit(string[] args)
    {
        try
        {
            var repoRoot = ResolveRepoRoot();
            if (repoRoot == null)
            {
                return null;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                return null;
            }

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            return proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveRepoRoot()
    {
        var current = new DirectoryInfo(_paths.ContentRoot);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private sealed record PublishInfoDocument(
        string? App,
        string? Tag,
        string? Commit,
        DateTimeOffset? CommitTime,
        DateTimeOffset? PublishTime,
        string? Configuration,
        bool? Dirty);

    private sealed record FrontendBuildInfoDocument(
        string? App,
        string? Tag,
        string? Commit,
        DateTimeOffset? CommitTime,
        DateTimeOffset? BuildTime,
        string? Configuration,
        bool? Dirty,
        string? Version);
}

public sealed record HostVersionInfo(
    string App,
    string? Tag,
    string? Commit,
    DateTimeOffset? CommitTime,
    DateTimeOffset? BuildTime,
    string? Configuration,
    bool? Dirty,
    string? Version,
    string? Layout);

public sealed record HostAboutSnapshot(
    HostVersionInfo Backend,
    HostVersionInfo Frontend,
    DiverAbiInfo DiverAbi);

/// <summary>Host 内置编译器/运行时的 DIVER 程序二进制 ABI（SemVer X.Y.Z）</summary>
public sealed record DiverAbiInfo(
    uint Magic,
    uint AbiVersion,
    int Major,
    int Minor,
    int Patch,
    string SemVer);
