using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoralinkerHost.Services;
using CoralinkerSDK;

namespace CoralinkerHost.Web;

public static class ApiRoutes
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/ping", () => JsonHelper.Json(new { ok = true }));

        // ============================================
        // 项目管理 API
        // ============================================

        app.MapGet("/api/project", (ProjectStore store) =>
        {
            var st = store.Get();
            return Results.Json(st, ProjectJson.Options);
        });

        app.MapPost("/api/project", async (ProjectStore store, HttpRequest req) =>
        {
            var state = await req.ReadFromJsonAsync<ProjectState>(ProjectJson.Options);
            if (state == null) return Results.BadRequest(new { error = "Invalid JSON" });
            store.Set(state);
            return Results.Ok();
        });

        app.MapPost("/api/project/new", (ProjectStore store) =>
        {
            store.CreateNew();
            return Results.Ok();
        });

        app.MapPost("/api/project/save", (ProjectStore store) =>
        {
            store.SaveToDisk();
            return Results.Ok();
        });

        // ============================================
        // 资源管理 API
        // ============================================

        app.MapPost("/api/assets/upload", async (ProjectStore store, HttpRequest req, CancellationToken ct) =>
        {
            if (!req.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data" });

            var form = await req.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file == null) return Results.BadRequest(new { error = "No file" });

            await using var s = file.OpenReadStream();
            var info = await store.SaveAssetAsync(file.FileName, s, ct);
            return JsonHelper.Json(info);
        });

        app.MapGet("/api/assets", (ProjectStore store) => JsonHelper.Json(store.ListAssets()));

        app.MapDelete("/api/assets/{name}", (ProjectStore store, string name) =>
        {
            return store.TryDeleteAsset(name) ? Results.Ok() : Results.NotFound();
        });

        // ============================================
        // 文件管理 API
        // ============================================

        app.MapGet("/api/files/tree", (FileTreeService tree, ILogger<FileTreeService> logger) =>
        {
            try
            {
                var result = tree.GetTree();
                return Results.Json(result, ProjectJson.Options);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "/api/files/tree failed");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        app.MapGet("/api/files/read", async (ProjectStore store, string path, CancellationToken ct) =>
        {
            var full = store.ResolveDataPath(path);
            if (!File.Exists(full)) return Results.NotFound();

            var fi = new FileInfo(full);
            if (fi.Length > 10 * 1024 * 1024) return Results.BadRequest(new { error = "File too large (>10MB)." });

            var ext = Path.GetExtension(full).ToLowerInvariant();
            var knownBinary = ext is ".bin" or ".dll" or ".exe" or ".pdb" or ".obj" or ".lib" or ".ilk" or ".exp";
            var knownText = ext is ".cs" or ".json" or ".xml" or ".txt" or ".md" or ".html" or ".css" or ".js" or ".map";

            bool isBinary = knownBinary;
            if (!knownBinary && !knownText)
            {
                var buffer = new byte[512];
                await using var fs = new FileStream(full, FileMode.Open, FileAccess.Read);
                var read = await fs.ReadAsync(buffer, 0, buffer.Length, ct);
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == 0)
                    {
                        isBinary = true;
                        break;
                    }
                }
            }

            if (!isBinary)
            {
                var txt = await File.ReadAllTextAsync(full, Encoding.UTF8, ct);
                return JsonHelper.Json(new { path, kind = "text", text = txt, sizeBytes = fi.Length });
            }

            var bytes = await File.ReadAllBytesAsync(full, ct);
            return JsonHelper.Json(new { path, kind = "binary", base64 = Convert.ToBase64String(bytes), sizeBytes = fi.Length });
        });

        app.MapPost("/api/files/write", async (ProjectStore store, FileWriteRequest req, CancellationToken ct) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Path))
                return Results.BadRequest(new { error = "Missing path" });

            var full = store.ResolveDataPath(req.Path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (string.Equals(req.Kind, "text", StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllTextAsync(full, req.Text ?? "", Encoding.UTF8, ct);
                return Results.Ok(new { ok = true });
            }
            if (string.Equals(req.Kind, "binary", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(req.Base64)) return Results.BadRequest(new { error = "Missing base64" });
                var bytes = Convert.FromBase64String(req.Base64);
                await File.WriteAllBytesAsync(full, bytes, ct);
                return Results.Ok(new { ok = true });
            }

            return Results.BadRequest(new { error = "Invalid kind" });
        });

        app.MapPost("/api/files/delete", (ProjectStore store, FileDeleteRequest req) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Path))
                return Results.BadRequest(new { error = "Missing path" });

            if (!req.Path.Replace('\\', '/').StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Delete allowed only under assets/." });

            var full = store.ResolveDataPath(req.Path);
            if (!File.Exists(full)) return Results.NotFound();
            File.Delete(full);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/files/newInput", async (ProjectStore store, NewInputRequest req, CancellationToken ct) =>
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Missing name" });

            var name = Path.GetFileName(req.Name);
            if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                name += ".cs";

            store.EnsureDataLayout();
            var full = Path.Combine(store.InputsDir, name);
            if (File.Exists(full)) return Results.BadRequest(new { error = "File already exists." });

            var content = req.Template ?? "using CartActivator;\n\nnamespace DiverTest\n{\n    // [LogicRunOnMCU(scanInterval = 50)]\n    public class MyLogic : LadderLogic<TestVehicle>\n    {\n        public override void Operation(int iteration)\n        {\n            // TODO\n        }\n    }\n}\n";
            await File.WriteAllTextAsync(full, content, Encoding.UTF8, ct);
            return Results.Ok(new { ok = true, path = $"assets/inputs/{name}" });
        });

        // ============================================
        // 构建 API
        // ============================================

        app.MapPost("/api/build", async (ProjectStore store, DiverBuildService builder, CancellationToken ct) =>
        {
            var state = store.Get();
            if (string.IsNullOrWhiteSpace(state.SelectedAsset))
            {
                var first = store.ListAssets().FirstOrDefault();
                if (first == null) return Results.BadRequest(new { error = "No assets uploaded." });
                state = state with { SelectedAsset = first.Name };
                store.Set(state);
            }

            var assetPath = Path.Combine(store.InputsDir, state.SelectedAsset!);
            if (!File.Exists(assetPath)) return Results.BadRequest(new { error = $"Asset not found: {state.SelectedAsset}" });
            var logicCs = await File.ReadAllTextAsync(assetPath, Encoding.UTF8, ct);

            try
            {
                var result = await builder.BuildFromLogicCsAsync(state.SelectedAsset!, logicCs, ct);
                var firstLogic = result.Artifacts.Keys.OrderBy(k => k).FirstOrDefault();
                var selectedFile = firstLogic != null
                    ? $"assets/generated/{firstLogic}.bin.json"
                    : null;
                store.Set(store.Get() with { LastBuildId = result.BuildId, SelectedFile = selectedFile });
                store.SaveToDisk();
                return JsonHelper.Json(new
                {
                    ok = true,
                    buildRoot = result.BuildRoot,
                    buildId = result.BuildId,
                    artifacts = result.Artifacts.Keys.OrderBy(k => k).ToArray()
                });
            }
            catch (BuildFailedException ex)
            {
                return Results.BadRequest(new
                {
                    ok = false,
                    error = ex.Message,
                    exitCode = ex.ExitCode,
                    logPath = ex.LogPath.Replace(store.HostRoot + Path.DirectorySeparatorChar, ""),
                    tail = ex.Tail
                });
            }
        });

        // ============================================
        // 节点管理 API
        // ============================================

        app.MapPost("/api/node/probe", async (HttpRequest req, RuntimeSessionService runtime, CancellationToken ct) =>
        {
            var payload = await req.ReadFromJsonAsync<NodeProbeRequest>(cancellationToken: ct);
            if (payload == null || string.IsNullOrWhiteSpace(payload.McuUri))
                return Results.BadRequest(new { ok = false, error = "Missing mcuUri" });

            var result = await runtime.ProbeNodeAsync(payload.McuUri, ct);
            if (result == null)
            {
                return JsonHelper.Json(new { ok = false, error = "Probe failed" });
            }

            return JsonHelper.Json(new
            {
                ok = true,
                version = new
                {
                    productionName = result.Version.ProductionName ?? "",
                    gitTag = result.Version.GitTag ?? "",
                    gitCommit = result.Version.GitCommit ?? "",
                    buildTime = result.Version.BuildTime ?? ""
                },
                layout = new
                {
                    digitalInputCount = (int)result.Layout.DigitalInputCount,
                    digitalOutputCount = (int)result.Layout.DigitalOutputCount,
                    portCount = (int)result.Layout.PortCount,
                    ports = result.Layout.GetValidPorts().Select(p => new
                    {
                        type = p.Type.ToString(),
                        name = p.Name
                    }).ToArray()
                }
            });
        });

        app.MapPost("/api/node/add", async (HttpRequest req, RuntimeSessionService runtime, CancellationToken ct) =>
        {
            var payload = await req.ReadFromJsonAsync<NodeProbeRequest>(cancellationToken: ct);
            if (payload == null || string.IsNullOrWhiteSpace(payload.McuUri))
                return Results.BadRequest(new { ok = false, error = "Missing mcuUri" });

            var uuid = await runtime.AddNodeAsync(payload.McuUri, ct);
            if (uuid == null)
            {
                return JsonHelper.Json(new { ok = false, error = "Add node failed" });
            }

            var info = DIVERSession.Instance.GetNodeInfo(uuid);
            return JsonHelper.Json(new
            {
                ok = true,
                uuid,
                nodeName = info?.NodeName,
                version = info?.Version,
                layout = info?.Layout
            });
        });

        app.MapPost("/api/node/{uuid}/remove", async (string uuid, RuntimeSessionService runtime, CancellationToken ct) =>
        {
            var result = await runtime.RemoveNodeAsync(uuid, ct);
            return JsonHelper.Json(new { ok = result });
        });

        app.MapPost("/api/node/{uuid}/configure", async (string uuid, HttpRequest req, CancellationToken ct) =>
        {
            var payload = await req.ReadFromJsonAsync<NodeConfigureRequest>(cancellationToken: ct);
            if (payload == null)
                return Results.BadRequest(new { ok = false, error = "Invalid request" });

            var settings = new NodeSettings
            {
                NodeName = payload.NodeName,
                PortConfigs = payload.PortConfigs?.Select(p => ParsePortConfig(p)).ToArray(),
                ExtraInfo = payload.ExtraInfo
            };

            var result = DIVERSession.Instance.ConfigureNode(uuid, settings);
            return JsonHelper.Json(new { ok = result });
        });

        app.MapPost("/api/node/{uuid}/program", async (string uuid, HttpRequest req, ProjectStore store, RuntimeSessionService runtime, CancellationToken ct) =>
        {
            // 读取原始请求体用于调试
            req.EnableBuffering();
            req.Body.Position = 0;
            using var reader = new StreamReader(req.Body, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync();
            req.Body.Position = 0;
            Console.WriteLine($"[API /program] uuid={uuid}, rawBody={rawBody}");
            
            var payload = await req.ReadFromJsonAsync<NodeProgramRequest>(cancellationToken: ct);
            Console.WriteLine($"[API /program] Parsed payload: LogicName={payload?.LogicName ?? "null"}");
            
            if (payload == null || string.IsNullOrWhiteSpace(payload.LogicName))
            {
                Console.WriteLine($"[API /program] ERROR: Missing logicName");
                return Results.BadRequest(new { ok = false, error = "Missing logicName" });
            }

            // 从 generated 目录读取编译产物
            var binPath = Path.Combine(store.GeneratedDir, $"{payload.LogicName}.bin");
            var metaPath = Path.Combine(store.GeneratedDir, $"{payload.LogicName}.bin.json");
            Console.WriteLine($"[API /program] binPath={binPath}, exists={File.Exists(binPath)}");
            Console.WriteLine($"[API /program] metaPath={metaPath}, exists={File.Exists(metaPath)}");

            if (!File.Exists(binPath) || !File.Exists(metaPath))
            {
                Console.WriteLine($"[API /program] ERROR: Build artifacts not found");
                return Results.BadRequest(new { ok = false, error = $"Build artifacts not found for '{payload.LogicName}'" });
            }

            var programBytes = await File.ReadAllBytesAsync(binPath, ct);
            var metaJson = await File.ReadAllTextAsync(metaPath, ct);

            var result = await runtime.ProgramNodeAsync(uuid, programBytes, metaJson, payload.LogicName, ct);
            Console.WriteLine($"[API /program] ProgramNodeAsync result={result}, size={programBytes.Length}");
            return JsonHelper.Json(new { ok = result, programSize = programBytes.Length });
        });

        app.MapGet("/api/node/{uuid}", (string uuid) =>
        {
            var info = DIVERSession.Instance.GetNodeInfo(uuid);
            if (info == null)
                return Results.NotFound(new { ok = false, error = "Node not found" });

            return JsonHelper.Json(new { ok = true, node = info });
        });

        app.MapGet("/api/node/{uuid}/state", (string uuid) =>
        {
            var state = DIVERSession.Instance.GetNodeState(uuid);
            if (state == null)
                return Results.NotFound(new { ok = false, error = "Node not found" });

            return JsonHelper.Json(new { ok = true, state });
        });

        app.MapGet("/api/nodes", () =>
        {
            var states = DIVERSession.Instance.GetNodeStates();
            var infos = states.Keys.Select(uuid => DIVERSession.Instance.GetNodeInfo(uuid)).ToArray();
            return JsonHelper.Json(new { ok = true, nodes = infos });
        });

        app.MapGet("/api/nodes/state", () =>
        {
            var states = DIVERSession.Instance.GetNodeStates();
            return JsonHelper.Json(new { ok = true, nodes = states.Values });
        });

        app.MapGet("/api/nodes/export", () =>
        {
            var data = DIVERSession.Instance.ExportNodes();
            return JsonHelper.Json(new { ok = true, nodes = data });
        });

        app.MapPost("/api/nodes/import", async (HttpRequest req, CancellationToken ct) =>
        {
            try
            {
                var payload = await req.ReadFromJsonAsync<NodesImportRequest>(cancellationToken: ct);
                if (payload?.Nodes == null)
                    return Results.BadRequest(new { ok = false, error = "Missing nodes" });

                DIVERSession.Instance.ImportNodes(payload.Nodes);
                return JsonHelper.Json(new { ok = true, count = payload.Nodes.Count });
            }
            catch (Exception ex)
            {
                return JsonHelper.Json(new { ok = false, error = ex.Message });
            }
        });

        app.MapPost("/api/nodes/clear", () =>
        {
            DIVERSession.Instance.RemoveAllNodes();
            return JsonHelper.Json(new { ok = true });
        });

        // ============================================
        // 会话控制 API
        // ============================================

        app.MapPost("/api/start", async (RuntimeSessionService runtime, CancellationToken ct) =>
        {
            try
            {
                var result = await runtime.StartAsync(ct);
                return JsonHelper.Json(new
                {
                    ok = result.Success,
                    totalNodes = result.TotalNodes,
                    successNodes = result.SuccessNodes,
                    errors = result.Errors.Select(e => new { uuid = e.UUID, nodeName = e.NodeName, error = e.Error })
                });
            }
            catch (Exception ex)
            {
                return JsonHelper.Json(new { ok = false, error = ex.Message });
            }
        });

        app.MapPost("/api/stop", async (RuntimeSessionService runtime, CancellationToken ct) =>
        {
            await runtime.StopAsync(ct);
            return JsonHelper.Json(new { ok = true });
        });

        app.MapGet("/api/session/state", () =>
        {
            var session = DIVERSession.Instance;
            return JsonHelper.Json(new
            {
                ok = true,
                state = session.State.ToString(),
                isRunning = session.IsRunning,
                nodeCount = session.GetNodeStates().Count
            });
        });

        // ============================================
        // 变量 API
        // ============================================

        app.MapGet("/api/variables", () =>
        {
            var fields = DIVERSession.Instance.GetAllCartFields();
            return JsonHelper.Json(new { ok = true, variables = fields.Values });
        });

        app.MapPost("/api/variable/set", async (HttpRequest req, TerminalBroadcaster term, CancellationToken ct) =>
        {
            try
            {
                var payload = await req.ReadFromJsonAsync<SetVariableRequest>(cancellationToken: ct);
                if (payload == null || string.IsNullOrWhiteSpace(payload.Name))
                    return Results.BadRequest(new { ok = false, error = "Missing variable name" });

                // 解析值
                object? parsedValue = payload.Value;
                if (payload.TypeHint != null)
                {
                    parsedValue = ValueParser.ParseValueByType(payload.Value, payload.TypeHint);
                }

                var result = DIVERSession.Instance.SetCartField(payload.Name, parsedValue ?? 0);
                if (!result)
                {
                    return Results.BadRequest(new { ok = false, error = "Cannot set LowerIO field" });
                }

                await term.LineAsync($"[var] Set {payload.Name} = {parsedValue}", ct);
                return JsonHelper.Json(new { ok = true, name = payload.Name, value = parsedValue });
            }
            catch (Exception ex)
            {
                return JsonHelper.Json(new { ok = false, error = ex.Message });
            }
        });

        app.MapGet("/api/variable/{name}", (string name) =>
        {
            var value = DIVERSession.Instance.GetCartField(name);
            if (value == null)
                return Results.NotFound(new { ok = false, error = "Variable not found" });

            return JsonHelper.Json(new { ok = true, name, value });
        });

        // ============================================
        // 日志 API
        // ============================================

        app.MapGet("/api/logs/nodes", () =>
        {
            var nodeIds = DIVERSession.Instance.GetLoggedNodeIds();
            return JsonHelper.Json(new { ok = true, nodes = nodeIds });
        });

        app.MapGet("/api/logs/node/{uuid}", (string uuid, long? afterSeq, int? maxCount) =>
        {
            var result = DIVERSession.Instance.GetNodeLogs(uuid, afterSeq, maxCount ?? 200);
            if (result == null)
                return Results.NotFound(new { ok = false, error = "Node not found" });

            return JsonHelper.Json(new
            {
                ok = true,
                uuid = result.UUID,
                latestSeq = result.LatestSeq,
                entries = result.Entries.Select(e => new
                {
                    seq = e.Seq,
                    timestamp = e.Timestamp.ToString("O"),
                    message = e.Message
                }),
                hasMore = result.HasMore
            });
        });

        app.MapPost("/api/logs/node/{uuid}/clear", (string uuid) =>
        {
            DIVERSession.Instance.ClearNodeLogs(uuid);
            return JsonHelper.Json(new { ok = true });
        });

        app.MapPost("/api/logs/clear", () =>
        {
            DIVERSession.Instance.ClearAllLogs();
            return JsonHelper.Json(new { ok = true });
        });

        // ============================================
        // 设备发现 API
        // ============================================

        app.MapGet("/api/ports", () =>
        {
            try
            {
                var ports = SerialPortResolver.ListAllPorts();
                return JsonHelper.Json(new { ok = true, ports });
            }
            catch (Exception ex)
            {
                return JsonHelper.Json(new { ok = false, error = ex.Message, ports = Array.Empty<string>() });
            }
        });

        app.MapGet("/api/logic/list", (ProjectStore store) =>
        {
            try
            {
                var logics = new List<object>();

                if (Directory.Exists(store.GeneratedDir))
                {
                    var binFiles = Directory.GetFiles(store.GeneratedDir, "*.bin");
                    foreach (var binPath in binFiles)
                    {
                        var jsonPath = binPath + ".json";
                        if (File.Exists(jsonPath))
                        {
                            var name = Path.GetFileNameWithoutExtension(binPath);
                            var binSize = new FileInfo(binPath).Length;
                            var jsonSize = new FileInfo(jsonPath).Length;

                            logics.Add(new
                            {
                                name,
                                binPath = Path.GetFileName(binPath),
                                jsonPath = Path.GetFileName(jsonPath),
                                binSize,
                                jsonSize
                            });
                        }
                    }
                }

                return JsonHelper.Json(new { ok = true, logics });
            }
            catch (Exception ex)
            {
                return JsonHelper.Json(new { ok = false, error = ex.Message, logics = Array.Empty<object>() });
            }
        });

        // ============================================
        // 导入/导出 API
        // ============================================

        app.MapGet("/api/project/export", (ProjectStore store) =>
        {
            try
            {
                var zipPath = Path.Combine(Path.GetTempPath(), $"coralinker-export-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    if (File.Exists(store.ProjectFile))
                    {
                        zip.CreateEntryFromFile(store.ProjectFile, "project.json");
                    }

                    if (Directory.Exists(store.InputsDir))
                    {
                        foreach (var file in Directory.GetFiles(store.InputsDir, "*.*", SearchOption.AllDirectories))
                        {
                            var relativePath = Path.GetRelativePath(store.DataDir, file).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, relativePath);
                        }
                    }

                    if (Directory.Exists(store.GeneratedDir))
                    {
                        foreach (var file in Directory.GetFiles(store.GeneratedDir, "*.*", SearchOption.AllDirectories))
                        {
                            var relativePath = Path.GetRelativePath(store.DataDir, file).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, relativePath);
                        }
                    }
                }

                var bytes = File.ReadAllBytes(zipPath);
                File.Delete(zipPath);

                return Results.File(bytes, "application/zip", $"coralinker-project-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        app.MapPost("/api/project/import", async (ProjectStore store, HttpRequest req) =>
        {
            try
            {
                var form = await req.ReadFormAsync();
                var file = form.Files.GetFile("file");

                if (file == null || file.Length == 0)
                {
                    return Results.BadRequest(new { error = "No file uploaded" });
                }

                var tempZipPath = Path.Combine(Path.GetTempPath(), $"coralinker-import-{Guid.NewGuid()}.zip");

                try
                {
                    using (var stream = File.Create(tempZipPath))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // 清空现有数据
                    DIVERSession.Instance.RemoveAllNodes();

                    if (Directory.Exists(store.InputsDir))
                    {
                        Directory.Delete(store.InputsDir, recursive: true);
                    }
                    if (Directory.Exists(store.GeneratedDir))
                    {
                        Directory.Delete(store.GeneratedDir, recursive: true);
                    }

                    Directory.CreateDirectory(store.InputsDir);
                    Directory.CreateDirectory(store.GeneratedDir);

                    using (var zip = ZipFile.OpenRead(tempZipPath))
                    {
                        foreach (var entry in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            var destPath = Path.Combine(store.DataDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                            var destDir = Path.GetDirectoryName(destPath);

                            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                            {
                                Directory.CreateDirectory(destDir);
                            }

                            entry.ExtractToFile(destPath, overwrite: true);
                        }
                    }

                    store.LoadFromDiskIfExists();

                    return Results.Ok(new { ok = true });
                }
                finally
                {
                    if (File.Exists(tempZipPath))
                    {
                        File.Delete(tempZipPath);
                    }
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery();

        // ============================================
        // 命令 API（保留用于调试）
        // ============================================

        app.MapPost("/api/command", async (TerminalBroadcaster term, HttpRequest req, CancellationToken ct) =>
        {
            var payload = await req.ReadFromJsonAsync<CommandRequest>(cancellationToken: ct);
            if (payload == null || string.IsNullOrWhiteSpace(payload.Command))
                return Results.BadRequest(new { error = "Missing command" });

            await term.LineAsync($"[cmd] {payload.Command}", ct);
            return Results.Ok(new { ok = true });
        });
    }

    private static MCUSerialBridgeCLR.PortConfig ParsePortConfig(PortConfigItem p)
    {
        if (string.Equals(p.Type, "CAN", StringComparison.OrdinalIgnoreCase))
        {
            return new MCUSerialBridgeCLR.CANPortConfig(p.Baud, p.RetryTimeMs ?? 10);
        }
        return new MCUSerialBridgeCLR.SerialPortConfig(p.Baud, p.ReceiveFrameMs ?? 0);
    }
}

// Request DTOs
public sealed record CommandRequest(string Command);
public sealed record FileWriteRequest(string Path, string Kind, string? Text, string? Base64);
public sealed record FileDeleteRequest(string Path);
public sealed record NewInputRequest(string Name, string? Template);
public sealed record NodeProbeRequest(string McuUri);
public sealed record NodeConfigureRequest(string? NodeName, PortConfigItem[]? PortConfigs, JsonObject? ExtraInfo);
public sealed record NodeProgramRequest(string LogicName);
public sealed record PortConfigItem(string Type, uint Baud, uint? ReceiveFrameMs, uint? RetryTimeMs);
public sealed record SetVariableRequest(string Name, object? Value, string? TypeHint);
public sealed record NodesImportRequest(Dictionary<string, NodeExportData> Nodes);

internal static class ValueParser
{
    public static object? ParseValueByType(object? value, string typeHint)
    {
        if (value == null) return null;
        var str = value.ToString() ?? "";

        return typeHint.ToLowerInvariant() switch
        {
            "boolean" or "bool" => bool.TryParse(str, out var b) ? b : str == "1",
            "byte" => byte.TryParse(str, out var by) ? by : (byte)0,
            "sbyte" => sbyte.TryParse(str, out var sb) ? sb : (sbyte)0,
            "int16" or "short" => short.TryParse(str, out var s) ? s : (short)0,
            "uint16" or "ushort" => ushort.TryParse(str, out var us) ? us : (ushort)0,
            "int32" or "int" => int.TryParse(str, out var i) ? i : 0,
            "uint32" or "uint" => uint.TryParse(str, out var ui) ? ui : 0u,
            "single" or "float" => float.TryParse(str, out var f) ? f : 0f,
            "byte[]" => ParseHexBytes(str),
            _ => value
        };
    }

    private static byte[] ParseHexBytes(string str)
    {
        str = str.Replace("0x", "").Replace(",", " ").Replace("-", " ");
        var parts = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new List<byte>();
        foreach (var part in parts)
        {
            if (byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out var b))
                bytes.Add(b);
        }
        return bytes.ToArray();
    }
}
