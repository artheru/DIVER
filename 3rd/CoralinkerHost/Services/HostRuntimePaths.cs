namespace CoralinkerHost.Services;

public enum HostRunLayout
{
    Development,
    Published
}

public sealed class HostRuntimePaths
{
    private readonly IHostEnvironment _env;

    public HostRuntimePaths(IHostEnvironment env)
    {
        _env = env;
        ContentRoot = Path.GetFullPath(env.ContentRootPath);
        DataDir = ResolveDataDir();
        CompilerResourcesDir = ResolveCompilerResourcesDir(out var layout);
        RunLayout = layout;
        RuntimeAssembliesDir = Path.Combine(DataDir, "runtime");
    }

    public string ContentRoot { get; }
    public string DataDir { get; }
    public string CompilerResourcesDir { get; }
    public string RuntimeAssembliesDir { get; }
    public HostRunLayout RunLayout { get; }

    public bool HasCompilerResources =>
        (File.Exists(Path.Combine(CompilerResourcesDir, "DiverCompiler.dll")) ||
         File.Exists(Path.Combine(CompilerResourcesDir, "DiverCompiler.exe"))) &&
        File.Exists(Path.Combine(CompilerResourcesDir, "RunOnMCU.cs"));

    public string Describe()
    {
        return $"layout={RunLayout}, contentRoot={ContentRoot}, dataDir={DataDir}, compilerResources={CompilerResourcesDir}";
    }

    private string ResolveDataDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CORALINKER_DATA_DIR");
        return string.IsNullOrWhiteSpace(fromEnv)
            ? Path.Combine(ContentRoot, "data")
            : Path.GetFullPath(fromEnv);
    }

    private string ResolveCompilerResourcesDir(out HostRunLayout layout)
    {
        var fromEnv = Environment.GetEnvironmentVariable("CORALINKER_COMPILER_RES_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            layout = HostRunLayout.Published;
            return Path.GetFullPath(fromEnv);
        }

        var published = Path.Combine(ContentRoot, "res", "compiler");
        if (File.Exists(Path.Combine(published, "DiverCompiler.dll")) ||
            File.Exists(Path.Combine(published, "DiverCompiler.exe")))
        {
            layout = HostRunLayout.Published;
            return published;
        }

        var portableRoot = Path.GetFullPath(Path.Combine(ContentRoot, "..", "..", "DiverCompilerPortable", "bin"));
        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var portableOutput = Path.Combine(portableRoot, configuration, "netstandard2.0");
            if (HasCompilerResourcesAt(portableOutput))
            {
                layout = HostRunLayout.Development;
                return portableOutput;
            }
        }

        var diverTest = Path.GetFullPath(Path.Combine(ContentRoot, "..", "..", "DiverTest"));
        if (HasCompilerResourcesAt(diverTest))
        {
            layout = HostRunLayout.Development;
            return diverTest;
        }

        layout = HostRunLayout.Development;
        return diverTest;
    }

    private static bool HasCompilerResourcesAt(string directory)
    {
        return (File.Exists(Path.Combine(directory, "DiverCompiler.dll")) ||
                File.Exists(Path.Combine(directory, "DiverCompiler.exe"))) &&
               File.Exists(Path.Combine(directory, "RunOnMCU.cs"));
    }
}
