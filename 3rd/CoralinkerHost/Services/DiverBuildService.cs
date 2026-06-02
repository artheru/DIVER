using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoralinkerHost.Services;

public sealed class DiverBuildService
{
    private readonly TerminalBroadcaster _terminal;
    private readonly ProjectStore _store;
    private readonly GitHistoryService _history;
    private readonly HostRuntimePaths _paths;
    private int _isBuilding;

    public DiverBuildService(TerminalBroadcaster terminal, ProjectStore store, GitHistoryService history, HostRuntimePaths paths)
    {
        _terminal = terminal;
        _store = store;
        _history = history;
        _paths = paths;
    }

    public bool IsBuilding => Volatile.Read(ref _isBuilding) != 0;

    public async Task<BuildResult> BuildFromLogicCsAsync(string logicFileName, string logicCs, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _isBuilding, 1) == 1)
        {
            throw new InvalidOperationException("Build is already running.");
        }

        try
        {
        _store.EnsureDataLayout();
        if (_history.HasDirtyInputs())
        {
            throw new InvalidOperationException("Input files have uncommitted changes. Save all inputs before building.");
        }

        var sourceHead = _history.GetHead()
            ?? throw new InvalidOperationException("No saved input commit found. Save an input file before building.");
        var buildTime = DateTimeOffset.Now;
        
        // 清空 Build 日志缓冲区
        _terminal.ClearBuildHistory();
        
        await _terminal.BuildLineAsync($"========== Starting Build Process ==========", ct);
        await _terminal.BuildLineAsync($"Target file: {logicFileName}", ct);
        await _terminal.BuildLineAsync($"Source length: {logicCs.Length} characters", ct);
        await _terminal.BuildLineAsync($"Source commit: {sourceHead.ShortHash} ({sourceHead.CommitTime:yyyy-MM-dd HH:mm:ss zzz})", ct);
        await _terminal.BuildLineAsync($"Build time: {buildTime:yyyy-MM-dd HH:mm:ss zzz}", ct);
        await _terminal.BuildLineAsync($"Runtime paths: {_paths.Describe()}", ct);
        EnsureDotnetSdkAvailable();
        EnsureCompilerResourcesAvailable();

        // Use single fixed build folder - clear it before each build
        var buildRoot = Path.Combine(_store.BuildsDir, "current");
        if (Directory.Exists(buildRoot))
        {
            await _terminal.BuildLineAsync($"Cleaning previous build folder: {buildRoot}", ct);
            
            // 多次尝试删除，处理 Windows Defender 文件锁
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    // 先尝试删除 obj 目录（最容易被锁定）
                    var objDir = Path.Combine(buildRoot, "proj", "obj");
                    if (Directory.Exists(objDir))
                    {
                        Directory.Delete(objDir, recursive: true);
                    }
                    
                    Directory.Delete(buildRoot, recursive: true);
                    await _terminal.BuildLineAsync($"Clean completed successfully", ct);
                    break;
                }
                catch (Exception) when (attempt < 3)
                {
                    await _terminal.BuildLineAsync($"Clean attempt {attempt} failed (file may be locked by antivirus), retrying in 2s...", ct);
                    await Task.Delay(2000, ct);
                }
                catch (Exception ex)
                {
                    await _terminal.BuildLineAsync($"Warning: Could not fully clean build folder: {ex.Message}", ct);
                    // 继续使用现有文件夹，MSBuild 会覆盖
                }
            }
        }
        
        Directory.CreateDirectory(buildRoot);
        await _terminal.BuildLineAsync($"Build root created: {buildRoot}", ct);
        var projDir = Path.Combine(buildRoot, "proj");
        Directory.CreateDirectory(projDir);

        var compilerDir = _paths.CompilerResourcesDir;
        await _terminal.BuildLineAsync($"Compiler resources: {compilerDir}", ct);

        var weaverPath = ResolveWeaverPath(compilerDir);
        var weaverFileName = Path.GetFileName(weaverPath);

        CopyCompilerDirectory(compilerDir, projDir);

        var extraMethods = Path.Combine(compilerDir, "extra_methods.txt");
        if (File.Exists(extraMethods))
            File.Copy(extraMethods, Path.Combine(projDir, "extra_methods.txt"), overwrite: true);

        var runOnMcu = Path.Combine(compilerDir, "RunOnMCU.cs");
        if (!File.Exists(runOnMcu))
            throw new FileNotFoundException("Missing RunOnMCU.cs.", runOnMcu);
        File.Copy(runOnMcu, Path.Combine(projDir, "RunOnMCU.cs"), overwrite: true);

        // Minimal references for common user inputs (e.g., DiverTest/TestLogic.cs) that derive from LocalDebugDIVERVehicle.
        var diverInterface = ResolveCompilerFile(compilerDir, "DIVERInterface.cs", Path.Combine("DIVER", "DIVERInterface.cs"));
        if (File.Exists(diverInterface))
            File.Copy(diverInterface, Path.Combine(projDir, "DIVERInterface.cs"), overwrite: true);

        var diverCommonUtils = ResolveCompilerFile(compilerDir, "DIVERCommonUtils.cs", Path.Combine("DIVER", "DIVERCommonUtils.cs"));
        if (File.Exists(diverCommonUtils))
            File.Copy(diverCommonUtils, Path.Combine(projDir, "DIVERCommonUtils.cs"), overwrite: true);

        var extensions = Path.Combine(compilerDir, "Extensions.cs");
        if (File.Exists(extensions))
            File.Copy(extensions, Path.Combine(projDir, "Extensions.cs"), overwrite: true);

        // Copy all user logic files from inputs
        if (Directory.Exists(_store.InputsDir))
        {
            var sourceFiles = Directory.GetFiles(_store.InputsDir, "*.cs", SearchOption.AllDirectories);
            await _terminal.BuildLineAsync($"Copying {sourceFiles.Length} source file(s) from inputs:", ct);
            foreach (var src in sourceFiles)
            {
                var rel = Path.GetRelativePath(_store.InputsDir, src);
                var dest = Path.Combine(projDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
                await _terminal.BuildLineAsync($"  + {rel}", ct);
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
        await _terminal.BuildLineAsync($"Using logic source: {logicFileName}", ct);

        // Minimal csproj to run Fody + DiverCompiler weaver without bringing in DiverTest's debug/runtime artifacts
        // Use timestamped assembly name to avoid file locking issues with AssemblyLoadContext
        var buildId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var assemblyName = "LogicBuild_" + buildId;
        var packageReferences = LoadBuildPackageReferences(compilerDir);
        var packageReferencesXml = BuildPackageReferencesXml(packageReferences);
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
                         __PACKAGE_REFERENCES__
                         <WeaverFiles Include="__WEAVER_FILE__" />
                       </ItemGroup>
                     </Project>
                     """;
        csproj = csproj.Replace("__ASSEMBLY_NAME__", assemblyName, StringComparison.Ordinal);
        csproj = csproj.Replace("__PACKAGE_REFERENCES__", packageReferencesXml, StringComparison.Ordinal);
        csproj = csproj.Replace("__WEAVER_FILE__", weaverFileName, StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(projDir, "LogicBuild.csproj"), csproj, Encoding.UTF8, ct);

        var offlineNuGetPackagesDir = ResolveNuGetPackagesDir(compilerDir);
        await _terminal.BuildLineAsync($"NuGet package source: {offlineNuGetPackagesDir}", ct);

        var nugetConfigPath = Path.Combine(projDir, "NuGet.Config");
        var nugetConfig = $$"""
                            <?xml version="1.0" encoding="utf-8"?>
                            <configuration>
                              <packageSources>
                                <clear />
                                <add key="CoralinkerOfflinePackages" value="{{XmlEscape(Path.GetFullPath(offlineNuGetPackagesDir))}}" />
                              </packageSources>
                            </configuration>
                            """;
        await File.WriteAllTextAsync(nugetConfigPath, nugetConfig, Encoding.UTF8, ct);

        var fodyWeavers = """
                          <Weavers xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="FodyWeavers.xsd">
                            <DiverCompiler />
                          </Weavers>
                          """;
        await File.WriteAllTextAsync(Path.Combine(projDir, "FodyWeavers.xml"), fodyWeavers, Encoding.UTF8, ct);

        await _terminal.BuildLineAsync($"Preparing MSBuild process...", ct);
        
        // 先执行 restore，这样可以看到进度
        await _terminal.BuildLineAsync($"---------- NuGet Restore ----------", ct);
        var restorePsi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"restore --configfile \"{nugetConfigPath}\" --verbosity minimal",
            WorkingDirectory = projDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        restorePsi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        restorePsi.Environment["DOTNET_NOLOGO"] = "true";
        
        var restoreExitCode = -1;
        using (var restoreProc = Process.Start(restorePsi))
        {
            if (restoreProc != null)
            {
                var restoreRing = new RingBuffer(50);
                var restoreStdout = ConsumeAsync(restoreProc.StandardOutput, null, restoreRing, ct);
                var restoreStderr = ConsumeAsync(restoreProc.StandardError, null, restoreRing, ct);
                await Task.WhenAll(restoreStdout, restoreStderr);
                await restoreProc.WaitForExitAsync(ct);
                restoreExitCode = restoreProc.ExitCode;
            }
        }
        if (restoreExitCode != 0)
        {
            await _terminal.BuildLineAsync($"RESTORE FAILED with exit code {restoreExitCode}", ct);
            throw new BuildFailedException(restoreExitCode, Path.Combine(buildRoot, "build.log"), Array.Empty<string>());
        }
        
        await _terminal.BuildLineAsync($"---------- MSBuild Compile ----------", ct);
        
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build -c Debug --no-restore --verbosity minimal",
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

        await _terminal.BuildLineAsync($"Executing: dotnet build -c Debug --no-restore", ct);
        await _terminal.BuildLineAsync($"Working directory: {projDir}", ct);
        
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

        await _terminal.BuildLineAsync($"---------- End MSBuild Output ----------", ct);
        
        if (proc.ExitCode != 0)
        {
            await _terminal.BuildLineAsync($"BUILD FAILED with exit code {proc.ExitCode}", ct);
            throw new BuildFailedException(proc.ExitCode, logPath, ring.Snapshot());
        }
        
        await _terminal.BuildLineAsync($"MSBuild completed successfully (exit code 0)", ct);

        var outDir = Path.Combine(projDir, "bin", "Debug", "net8.0");
        var dllPath = Path.Combine(outDir, assemblyName + ".dll");
        await _terminal.BuildLineAsync($"Looking for output assembly: {dllPath}", ct);
        
        if (!File.Exists(dllPath))
            throw new FileNotFoundException("Build succeeded but output DLL not found.", dllPath);
        
        var dllSize = new FileInfo(dllPath).Length;
        await _terminal.BuildLineAsync($"Output assembly found: {dllSize:N0} bytes", ct);

        // Clear previous generated artifacts before extracting new ones
        if (Directory.Exists(_store.GeneratedDir))
        {
            await _terminal.BuildLineAsync($"Clearing previous generated artifacts from: {_store.GeneratedDir}", ct);
            foreach (var f in Directory.GetFiles(_store.GeneratedDir, "*", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(f); } catch { /* ignore locked files */ }
            }
        }
        Directory.CreateDirectory(_store.GeneratedDir);

        // Extract artifacts directly into generated folder (no subfolders)
        await _terminal.BuildLineAsync($"Extracting DIVER artifacts from assembly...", ct);
        var versionInfo = new BuildVersionInfo(
            sourceHead.Hash,
            sourceHead.ShortHash,
            sourceHead.CommitTime,
            buildTime,
            buildId
        );
        var artifacts = ExtractArtifacts(dllPath, _store.GeneratedDir, versionInfo);
        var rootAssemblyPath = CopyRootRuntimeAssembly(dllPath, _store.GeneratedDir, buildId, assemblyName);
        var rootLogics = ExtractRootLogics(rootAssemblyPath, _store.GeneratedDir, versionInfo);
        
        foreach (var kv in artifacts)
        {
            var art = kv.Value;
            await _terminal.BuildLineAsync($"  + {kv.Key}:", ct);
            await _terminal.BuildLineAsync($"      .bin:          {new FileInfo(art.BinPath).Length:N0} bytes", ct);
            await _terminal.BuildLineAsync($"      .bin.json:     {new FileInfo(art.MetaJsonPath).Length:N0} bytes", ct);
            await _terminal.BuildLineAsync($"      .diver:        {new FileInfo(art.DiverPath).Length:N0} bytes", ct);
            await _terminal.BuildLineAsync($"      .diver.map.json: {new FileInfo(art.DiverMapPath).Length:N0} bytes", ct);
        }
        
        await _terminal.BuildLineAsync($"========== Build Complete ==========", ct);
        await _terminal.BuildLineAsync($"Successfully extracted {artifacts.Count} logic artifact set(s)", ct);
        await _terminal.BuildLineAsync($"Successfully extracted {rootLogics.Count} root logic definition(s)", ct);

        return new BuildResult(buildRoot, projDir, dllPath, artifacts)
        {
            BuildId = buildId,
            SourceCommit = sourceHead.Hash,
            SourceCommitShort = sourceHead.ShortHash,
            SourceCommitTime = sourceHead.CommitTime,
            BuildTime = buildTime,
            RootLogics = rootLogics
        };
        }
        finally
        {
            Volatile.Write(ref _isBuilding, 0);
        }
    }

    /// <summary>
    /// 消费 MSBuild 输出流，发送到 Build 日志面板
    /// </summary>
    private async Task ConsumeAsync(StreamReader reader, StreamWriter? log, RingBuffer ring, CancellationToken ct)
    {
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (log != null)
            {
                await log.WriteLineAsync(line);
            }
            ring.Add(line);
            // MSBuild 输出不加时间戳，直接发送
            await _terminal.BuildLineRawAsync(line, ct);
        }
    }

    private void EnsureCompilerResourcesAvailable()
    {
        if (!_paths.HasCompilerResources)
        {
            throw new InvalidOperationException(
                $"Compiler resources are incomplete. Expected DiverCompiler.dll or DiverCompiler.exe and RunOnMCU.cs under {_paths.CompilerResourcesDir}.");
        }
    }

    private static void EnsureDotnetSdkAvailable()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--list-sdks",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet SDK check.");
        var output = proc.StandardOutput.ReadToEnd();
        var error = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException($"dotnet SDK is required for Build but was not found. {error}".Trim());
        }
    }

    private static string ResolveCompilerFile(string compilerDir, string publishedName, string developmentRelative)
    {
        var published = Path.Combine(compilerDir, publishedName);
        if (File.Exists(published)) return published;
        return Path.Combine(compilerDir, developmentRelative);
    }

    private string ResolveNuGetPackagesDir(string compilerDir)
    {
        var packaged = Path.Combine(compilerDir, "nuget-packages");
        if (Directory.Exists(packaged)) return packaged;

        if (_paths.RunLayout == HostRunLayout.Published)
        {
            throw new DirectoryNotFoundException(
                $"Missing offline NuGet packages directory: {packaged}. Re-publish Host so res/compiler/nuget-packages is included.");
        }

        var candidates = new List<string>();

        var fromEnv = Environment.GetEnvironmentVariable("CORALINKER_NUGET_PACKAGES_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv)) candidates.Add(fromEnv);

        var nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(nugetPackages)) candidates.Add(nugetPackages);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            candidates.Add(Path.Combine(userProfile, ".nuget", "packages"));
        }

        foreach (var candidate in candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException(
            $"Missing NuGet package source for development build. Expected packaged source at {packaged}, or set CORALINKER_NUGET_PACKAGES_DIR / NUGET_PACKAGES to an existing NuGet packages directory.");
    }

    private static string ResolveWeaverPath(string compilerDir)
    {
        var dll = Path.Combine(compilerDir, "DiverCompiler.dll");
        if (File.Exists(dll)) return dll;

        var exe = Path.Combine(compilerDir, "DiverCompiler.exe");
        if (File.Exists(exe)) return exe;

        throw new FileNotFoundException("Missing DiverCompiler.dll or DiverCompiler.exe.", dll);
    }

    private static string XmlEscape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static IReadOnlyList<BuildPackageReference> LoadBuildPackageReferences(string compilerDir)
    {
        var configPath = Path.Combine(compilerDir, "build-packages.json");
        if (!File.Exists(configPath))
        {
            return DefaultBuildPackageReferences();
        }

        var json = File.ReadAllText(configPath, Encoding.UTF8);
        var packages = JsonSerializer.Deserialize<List<BuildPackageReference>>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (packages == null || packages.Count == 0)
        {
            throw new InvalidOperationException($"No package references found in {configPath}.");
        }

        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.Include) || string.IsNullOrWhiteSpace(package.Version))
            {
                throw new InvalidOperationException(
                    $"Invalid package reference in {configPath}. Each item must include 'include' and 'version'.");
            }
        }

        return packages;
    }

    private static IReadOnlyList<BuildPackageReference> DefaultBuildPackageReferences()
    {
        return new[]
        {
            new BuildPackageReference("Fody", "6.6.4", "all", "runtime; build; native; contentfiles; analyzers; buildtransitive"),
            new BuildPackageReference("Newtonsoft.Json", "13.0.3"),
            new BuildPackageReference("System.IO.Ports", "9.0.3"),
            new BuildPackageReference("System.Management", "9.0.4")
        };
    }

    private static string BuildPackageReferencesXml(IReadOnlyList<BuildPackageReference> packages)
    {
        var sb = new StringBuilder();
        foreach (var package in packages)
        {
            var include = XmlEscape(package.Include);
            var version = XmlEscape(package.Version);
            if (string.IsNullOrWhiteSpace(package.PrivateAssets) &&
                string.IsNullOrWhiteSpace(package.IncludeAssets))
            {
                sb.Append("                         ");
                sb.Append("<PackageReference Include=\"");
                sb.Append(include);
                sb.Append("\" Version=\"");
                sb.Append(version);
                sb.AppendLine("\" />");
                continue;
            }

            sb.Append("                         ");
            sb.Append("<PackageReference Include=\"");
            sb.Append(include);
            sb.Append("\" Version=\"");
            sb.Append(version);
            sb.AppendLine("\">");
            if (!string.IsNullOrWhiteSpace(package.PrivateAssets))
            {
                sb.Append("                           <PrivateAssets>");
                sb.Append(XmlEscape(package.PrivateAssets));
                sb.AppendLine("</PrivateAssets>");
            }
            if (!string.IsNullOrWhiteSpace(package.IncludeAssets))
            {
                sb.Append("                           <IncludeAssets>");
                sb.Append(XmlEscape(package.IncludeAssets));
                sb.AppendLine("</IncludeAssets>");
            }
            sb.AppendLine("                         </PackageReference>");
        }

        return sb.ToString().TrimEnd();
    }

    private static void CopyCompilerDirectory(string sourceDir, string destinationDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static IReadOnlyDictionary<string, BuildArtifacts> ExtractArtifacts(
        string builtDllPath,
        string outDir,
        BuildVersionInfo versionInfo)
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
                var buildInfoPath = Path.Combine(outDir, ln + ".build.json");
                WriteBuildMetadata(buildInfoPath, versionInfo);
                dict[ln] = new BuildArtifacts(ln, bin, meta, diver, map, buildInfoPath);
            }
        }

        return dict;
    }

    private static IReadOnlyDictionary<string, RootLogicMetadata> ExtractRootLogics(
        string builtDllPath,
        string outDir,
        BuildVersionInfo versionInfo)
    {
        var dict = new Dictionary<string, RootLogicMetadata>(StringComparer.OrdinalIgnoreCase);
        var alc = new AssemblyLoadContext("RootLogicRead-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        Assembly asm;
        try
        {
            asm = alc.LoadFromAssemblyPath(builtDllPath);
            foreach (var type in asm.GetTypes())
            {
                var attr = type.GetCustomAttributes()
                    .FirstOrDefault(a => a.GetType().Name == "LogicRunOnRootAttribute");
                if (attr == null || !TryGetRootCartType(type, out var cartType)) continue;

                var scanInterval = (int?)attr.GetType().GetField("scanInterval")?.GetValue(attr) ?? 20;
                var cartFields = cartType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(f => f.GetCustomAttributes().Any(a => a.GetType().Name is "AsUpperIO" or "AsLowerIO"))
                    .Select(BuildFieldMetadata)
                    .ToArray();
                var controlFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(f => f.GetCustomAttributes().Any(a => a.GetType().Name == "AsControlItem"))
                    .Select(BuildControlFieldMetadata)
                    .ToArray();

                var meta = new RootLogicMetadata(
                    type.Name,
                    type.FullName ?? type.Name,
                    Path.GetRelativePath(outDir, builtDllPath).Replace('\\', '/'),
                    scanInterval,
                    versionInfo.SourceCommit,
                    versionInfo.SourceCommitShort,
                    versionInfo.SourceCommitTime,
                    versionInfo.BuildTime,
                    versionInfo.BuildId,
                    cartFields,
                    controlFields
                );
                var outPath = Path.Combine(outDir, type.Name + ".root.json");
                File.WriteAllText(
                    outPath,
                    JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    Encoding.UTF8
                );
                dict[type.Name] = meta;
            }
        }
        finally
        {
            alc.Unload();
        }

        return dict;
    }

    private static string CopyRootRuntimeAssembly(string builtDllPath, string generatedDir, string buildId, string assemblyName)
    {
        var sourceDir = Path.GetDirectoryName(builtDllPath)
            ?? throw new InvalidOperationException("Built DLL has no parent directory.");
        var runtimeDir = Path.Combine(generatedDir, "assemblies", buildId);
        Directory.CreateDirectory(runtimeDir);

        foreach (var file in Directory.GetFiles(sourceDir, assemblyName + ".*", SearchOption.TopDirectoryOnly))
        {
            var dest = Path.Combine(runtimeDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        return Path.Combine(runtimeDir, Path.GetFileName(builtDllPath));
    }

    private static bool TryGetRootCartType(Type type, out Type cartType)
    {
        var current = type;
        while (current != null)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition().FullName?.StartsWith("CartActivator.RootLogic`1", StringComparison.Ordinal) == true)
            {
                cartType = current.GetGenericArguments()[0];
                return true;
            }
            current = current.BaseType;
        }
        cartType = typeof(object);
        return false;
    }

    private static RootFieldMetadata BuildFieldMetadata(FieldInfo field)
    {
        var direction = field.GetCustomAttributes().Any(a => a.GetType().Name == "AsUpperIO")
            ? "upper"
            : field.GetCustomAttributes().Any(a => a.GetType().Name == "AsLowerIO")
                ? "lower"
                : throw new InvalidOperationException($"Root cart field {field.Name} has no declared direction.");
        return new RootFieldMetadata(field.Name, field.FieldType.Name, TypeIdOf(field.FieldType), direction);
    }

    private static RootFieldMetadata BuildControlFieldMetadata(FieldInfo field)
    {
        if (TypeIdOf(field.FieldType) < 0)
        {
            throw new InvalidOperationException($"Root control field {field.Name} type {field.FieldType.Name} is not supported.");
        }
        return new RootFieldMetadata(field.Name, field.FieldType.Name, TypeIdOf(field.FieldType), "control");
    }

    private static int TypeIdOf(Type t)
    {
        if (t == typeof(bool)) return 0;
        if (t == typeof(byte)) return 1;
        if (t == typeof(sbyte)) return 2;
        if (t == typeof(char)) return 3;
        if (t == typeof(short)) return 4;
        if (t == typeof(ushort)) return 5;
        if (t == typeof(int)) return 6;
        if (t == typeof(uint)) return 7;
        if (t == typeof(float)) return 8;
        return -1;
    }

    private static void WriteBuildMetadata(string jsonPath, BuildVersionInfo versionInfo)
    {
        var json = JsonSerializer.Serialize(
            versionInfo,
            new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );
        File.WriteAllText(jsonPath, json, Encoding.UTF8);
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
    public string? SourceCommit { get; init; }
    public string? SourceCommitShort { get; init; }
    public DateTimeOffset? SourceCommitTime { get; init; }
    public DateTimeOffset? BuildTime { get; init; }
    public IReadOnlyDictionary<string, RootLogicMetadata> RootLogics { get; init; } = new Dictionary<string, RootLogicMetadata>();
}

public sealed record BuildVersionInfo(
    string SourceCommit,
    string SourceCommitShort,
    DateTimeOffset? SourceCommitTime,
    DateTimeOffset BuildTime,
    string BuildId);

public sealed record BuildPackageReference(
    string Include,
    string Version,
    string? PrivateAssets = null,
    string? IncludeAssets = null);

public sealed record RootFieldMetadata(string Name, string Type, int TypeId, string Direction);

public sealed record RootLogicMetadata(
    string Name,
    string TypeName,
    string AssemblyPath,
    int ScanInterval,
    string SourceCommit,
    string SourceCommitShort,
    DateTimeOffset? SourceCommitTime,
    DateTimeOffset BuildTime,
    string BuildId,
    RootFieldMetadata[] CartFields,
    RootFieldMetadata[] ControlFields);

public sealed record BuildArtifacts(
    string LogicName,
    string BinPath,
    string MetaJsonPath,
    string DiverPath,
    string DiverMapPath,
    string BuildInfoPath);


