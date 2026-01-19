using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace CoralinkerHost.Services;

public sealed class DiverBuildService
{
    private readonly TerminalBroadcaster _terminal;
    private readonly ProjectStore _store;

    public DiverBuildService(TerminalBroadcaster terminal, ProjectStore store)
    {
        _terminal = terminal;
        _store = store;
    }

    public async Task<BuildResult> BuildFromLogicCsAsync(string logicFileName, string logicCs, CancellationToken ct)
    {
        _store.EnsureDataLayout();
        
        await _terminal.LineAsync($"[build] ========== Starting Build Process ==========", ct);
        await _terminal.LineAsync($"[build] Target file: {logicFileName}", ct);
        await _terminal.LineAsync($"[build] Source length: {logicCs.Length} characters", ct);

        // Use single fixed build folder - clear it before each build
        var buildRoot = Path.Combine(_store.BuildsDir, "current");
        if (Directory.Exists(buildRoot))
        {
            await _terminal.LineAsync($"[build] Cleaning previous build folder: {buildRoot}", ct);
            try
            {
                Directory.Delete(buildRoot, recursive: true);
                await _terminal.LineAsync($"[build] Clean completed successfully", ct);
            }
            catch (Exception ex)
            {
                await _terminal.LineAsync($"[build] Warning: Could not fully clean build folder: {ex.Message}", ct);
            }
        }
        
        Directory.CreateDirectory(buildRoot);
        await _terminal.LineAsync($"[build] Build root created: {buildRoot}", ct);
        var projDir = Path.Combine(buildRoot, "proj");
        Directory.CreateDirectory(projDir);

        // Copy only the essentials from the repo (no bin/obj/pdb/etc).
        // Repo layout: <repo>/3rd/CoralinkerHost -> repo root is ../../ from HostRoot
        var repoRoot = Path.GetFullPath(Path.Combine(_store.HostRoot, "..", ".."));
        var diverTestDir = Path.Combine(repoRoot, "DiverTest");

        var weaverExe = Path.Combine(diverTestDir, "DiverCompiler.exe");
        if (!File.Exists(weaverExe))
            throw new FileNotFoundException("Missing DiverCompiler.exe (expected in DiverTest).", weaverExe);

        File.Copy(weaverExe, Path.Combine(projDir, "DiverCompiler.exe"), overwrite: true);

        var extraMethods = Path.Combine(diverTestDir, "extra_methods.txt");
        if (File.Exists(extraMethods))
            File.Copy(extraMethods, Path.Combine(projDir, "extra_methods.txt"), overwrite: true);

        var runOnMcu = Path.Combine(diverTestDir, "RunOnMCU.cs");
        if (!File.Exists(runOnMcu))
            throw new FileNotFoundException("Missing RunOnMCU.cs (expected in DiverTest).", runOnMcu);
        File.Copy(runOnMcu, Path.Combine(projDir, "RunOnMCU.cs"), overwrite: true);

        // Minimal references for common user inputs (e.g., DiverTest/TestLogic.cs) that derive from LocalDebugDIVERVehicle.
        var diverInterface = Path.Combine(diverTestDir, "DIVER", "DIVERInterface.cs");
        if (File.Exists(diverInterface))
            File.Copy(diverInterface, Path.Combine(projDir, "DIVERInterface.cs"), overwrite: true);

        var extensions = Path.Combine(diverTestDir, "Extensions.cs");
        if (File.Exists(extensions))
            File.Copy(extensions, Path.Combine(projDir, "Extensions.cs"), overwrite: true);

        // Copy all user logic files from inputs
        if (Directory.Exists(_store.InputsDir))
        {
            var sourceFiles = Directory.GetFiles(_store.InputsDir, "*.cs", SearchOption.AllDirectories);
            await _terminal.LineAsync($"[build] Copying {sourceFiles.Length} source file(s) from inputs:", ct);
            foreach (var src in sourceFiles)
            {
                var rel = Path.GetRelativePath(_store.InputsDir, src);
                var dest = Path.Combine(projDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
                await _terminal.LineAsync($"[build]   + {rel}", ct);
            }
        }
        else
        {
             // Fallback if inputs dir doesn't exist (unlikely)
             var logicPath = Path.Combine(projDir, Path.GetFileName(logicFileName));
             await File.WriteAllTextAsync(logicPath, logicCs, Encoding.UTF8, ct);
        }

        // Ensure the selected logic file is updated with current content (if different from disk)
        // logicFileName is relative to InputsDir
        var selectedDest = Path.Combine(projDir, logicFileName);
        // Only write if we haven't just copied it (technically we did, but this ensures logicCs is used if passed explicitly)
        // Since ApiRoutes reads from disk, this is redundant but safe.
        await File.WriteAllTextAsync(selectedDest, logicCs, Encoding.UTF8, ct);
        await _terminal.LineAsync($"[build] Using logic source: {logicFileName}", ct);

        // Minimal csproj to run Fody + DiverCompiler weaver without bringing in DiverTest's debug/runtime artifacts
        // Use timestamped assembly name to avoid file locking issues with AssemblyLoadContext
        var buildId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var assemblyName = "LogicBuild_" + buildId;
        var csproj = """
                     <Project Sdk="Microsoft.NET.Sdk">
                       <PropertyGroup>
                         <TargetFramework>net8.0</TargetFramework>
                         <ImplicitUsings>enable</ImplicitUsings>
                         <Nullable>disable</Nullable>
                         <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                         <AssemblyName>__ASSEMBLY_NAME__</AssemblyName>
                       </PropertyGroup>
                       <ItemGroup>
                         <PackageReference Include="Fody" Version="6.6.4">
                           <PrivateAssets>all</PrivateAssets>
                           <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
                         </PackageReference>
                         <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                         <PackageReference Include="System.IO.Ports" Version="9.0.3" />
                         <PackageReference Include="System.Management" Version="9.0.4" />
                         <WeaverFiles Include="DiverCompiler.exe" />
                       </ItemGroup>
                     </Project>
                     """;
        csproj = csproj.Replace("__ASSEMBLY_NAME__", assemblyName, StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(projDir, "LogicBuild.csproj"), csproj, Encoding.UTF8, ct);

        var fodyWeavers = """
                          <Weavers xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="FodyWeavers.xsd">
                            <DiverCompiler />
                          </Weavers>
                          """;
        await File.WriteAllTextAsync(Path.Combine(projDir, "FodyWeavers.xml"), fodyWeavers, Encoding.UTF8, ct);

        await _terminal.LineAsync($"[build] Preparing MSBuild process...", ct);
        
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build -c Debug",
            WorkingDirectory = projDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        
        // Set environment to force UTF-8 output from dotnet
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        psi.Environment["DOTNET_NOLOGO"] = "true";

        await _terminal.LineAsync($"[build] Executing: dotnet build -c Debug", ct);
        await _terminal.LineAsync($"[build] Working directory: {projDir}", ct);
        await _terminal.LineAsync($"[build] ---------- MSBuild Output ----------", ct);
        
        using var proc = Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start dotnet build.");

        var logPath = Path.Combine(buildRoot, "build.log");
        await using var logFs = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var logSw = new StreamWriter(logFs, Encoding.UTF8) { AutoFlush = true };

        var ring = new RingBuffer(200);
        var stdout = ConsumeAsync(proc.StandardOutput, logSw, ring, ct);
        var stderr = ConsumeAsync(proc.StandardError, logSw, ring, ct);

        await Task.WhenAll(stdout, stderr);
        await proc.WaitForExitAsync(ct);

        await _terminal.LineAsync($"[build] ---------- End MSBuild Output ----------", ct);
        
        if (proc.ExitCode != 0)
        {
            await _terminal.LineAsync($"[build] BUILD FAILED with exit code {proc.ExitCode}", ct);
            throw new BuildFailedException(proc.ExitCode, logPath, ring.Snapshot());
        }
        
        await _terminal.LineAsync($"[build] MSBuild completed successfully (exit code 0)", ct);

        var outDir = Path.Combine(projDir, "bin", "Debug", "net8.0");
        var dllPath = Path.Combine(outDir, assemblyName + ".dll");
        await _terminal.LineAsync($"[build] Looking for output assembly: {dllPath}", ct);
        
        if (!File.Exists(dllPath))
            throw new FileNotFoundException("Build succeeded but output DLL not found.", dllPath);
        
        var dllSize = new FileInfo(dllPath).Length;
        await _terminal.LineAsync($"[build] Output assembly found: {dllSize:N0} bytes", ct);

        // Clear previous generated artifacts before extracting new ones
        if (Directory.Exists(_store.GeneratedDir))
        {
            await _terminal.LineAsync($"[build] Clearing previous generated artifacts from: {_store.GeneratedDir}", ct);
            foreach (var f in Directory.GetFiles(_store.GeneratedDir, "*", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(f); } catch { /* ignore locked files */ }
            }
        }
        Directory.CreateDirectory(_store.GeneratedDir);

        // Extract artifacts directly into generated folder (no subfolders)
        await _terminal.LineAsync($"[build] Extracting DIVER artifacts from assembly...", ct);
        var artifacts = ExtractArtifacts(dllPath, _store.GeneratedDir);
        
        foreach (var kv in artifacts)
        {
            var art = kv.Value;
            await _terminal.LineAsync($"[build]   + {kv.Key}:", ct);
            await _terminal.LineAsync($"[build]       .bin:          {new FileInfo(art.BinPath).Length:N0} bytes", ct);
            await _terminal.LineAsync($"[build]       .bin.json:     {new FileInfo(art.MetaJsonPath).Length:N0} bytes", ct);
            await _terminal.LineAsync($"[build]       .diver:        {new FileInfo(art.DiverPath).Length:N0} bytes", ct);
            await _terminal.LineAsync($"[build]       .diver.map.json: {new FileInfo(art.DiverMapPath).Length:N0} bytes", ct);
        }
        
        await _terminal.LineAsync($"[build] ========== Build Complete ==========", ct);
        await _terminal.LineAsync($"[build] Successfully extracted {artifacts.Count} logic artifact set(s)", ct);

        return new BuildResult(buildRoot, projDir, dllPath, artifacts) { BuildId = buildId };
    }

    private async Task ConsumeAsync(StreamReader reader, StreamWriter log, RingBuffer ring, CancellationToken ct)
    {
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            await log.WriteLineAsync(line);
            ring.Add(line);
            await _terminal.LineAsync(line, ct);
        }
    }

    private static IReadOnlyDictionary<string, BuildArtifacts> ExtractArtifacts(string builtDllPath, string outDir)
    {
        var dict = new Dictionary<string, BuildArtifacts>(StringComparer.OrdinalIgnoreCase);

        // IMPORTANT: load into an isolated collectible context so repeated builds (same assembly name) don't collide.
        var alc = new AssemblyLoadContext("ArtifactRead-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        Assembly asm;
        try
        {
            asm = alc.LoadFromAssemblyPath(builtDllPath);
        }
        catch
        {
            alc.Unload();
            throw;
        }

        string[] resNames;
        try
        {
            resNames = asm.GetManifestResourceNames();
        }
        catch
        {
            alc.Unload();
            throw;
        }

        static string? LogicNameFrom(string res)
        {
            if (res.EndsWith(".bin.json", StringComparison.OrdinalIgnoreCase))
                return res[..^".bin.json".Length];
            if (res.EndsWith(".diver.map.json", StringComparison.OrdinalIgnoreCase))
                return res[..^".diver.map.json".Length];
            if (res.EndsWith(".diver", StringComparison.OrdinalIgnoreCase))
                return res[..^".diver".Length];
            if (res.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                return res[..^".bin".Length];
            return null;
        }

        foreach (var res in resNames)
        {
            var ln = LogicNameFrom(res);
            if (ln == null) continue;

            using var s = asm.GetManifestResourceStream(res);
            if (s == null) continue;
            var outPath = Path.Combine(outDir, res);
            using var fs = File.Create(outPath);
            s.CopyTo(fs);
        }

        // Unload context to avoid file locks / name collisions across builds.
        alc.Unload();

        // Build artifacts set for each logic name found
        var allFiles = Directory.GetFiles(outDir);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in allFiles)
        {
            var name = Path.GetFileName(f);
            var ln = LogicNameFrom(name);
            if (ln != null) candidates.Add(ln);
        }

        foreach (var ln in candidates.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var bin = Path.Combine(outDir, ln + ".bin");
            var meta = Path.Combine(outDir, ln + ".bin.json");
            var diver = Path.Combine(outDir, ln + ".diver");
            var map = Path.Combine(outDir, ln + ".diver.map.json");
            if (File.Exists(bin) && File.Exists(meta) && File.Exists(diver) && File.Exists(map))
            {
                dict[ln] = new BuildArtifacts(ln, bin, meta, diver, map);
            }
        }

        return dict;
    }
}

public sealed class BuildFailedException : Exception
{
    public int ExitCode { get; }
    public string LogPath { get; }
    public IReadOnlyList<string> Tail { get; }

    public BuildFailedException(int exitCode, string logPath, IReadOnlyList<string> tail)
        : base($"Build failed (exit {exitCode}).")
    {
        ExitCode = exitCode;
        LogPath = logPath;
        Tail = tail;
    }
}

internal sealed class RingBuffer
{
    private readonly string[] _buf;
    private int _count;
    private int _idx;

    public RingBuffer(int capacity)
    {
        _buf = new string[Math.Max(1, capacity)];
    }

    public void Add(string line)
    {
        _buf[_idx] = line;
        _idx = (_idx + 1) % _buf.Length;
        _count = Math.Min(_count + 1, _buf.Length);
    }

    public IReadOnlyList<string> Snapshot()
    {
        var outList = new List<string>(_count);
        var start = (_idx - _count + _buf.Length) % _buf.Length;
        for (int i = 0; i < _count; i++)
        {
            outList.Add(_buf[(start + i) % _buf.Length]);
        }
        return outList;
    }
}

public sealed record BuildResult(
    string BuildRoot,
    string DiverTestDir,
    string? OutputDllPath,
    IReadOnlyDictionary<string, BuildArtifacts> Artifacts)
{
    public string? BuildId { get; init; }
}

public sealed record BuildArtifacts(
    string LogicName,
    string BinPath,
    string MetaJsonPath,
    string DiverPath,
    string DiverMapPath);


