using System.IO.Compression;
using System.Text;
using CoralinkerHost.Services;

namespace CoralinkerHost.Web;

public static class ApiRoutes
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/ping", () => Results.Json(new { ok = true }));

        app.MapGet("/api/project", (ProjectStore store) =>
        {
            // Migration/sanity: older code stored SelectedFile as assets/generated/{buildId}/{logic}.bin.json
            // but generated/ is now flat (no subfolders). If the stored path doesn't exist, try to repair it.
            var st = store.Get();
            if (!string.IsNullOrWhiteSpace(st.SelectedFile))
            {
                try
                {
                    var full = store.ResolveDataPath(st.SelectedFile!);
                    if (!System.IO.File.Exists(full))
                    {
                        // If it looks like assets/generated/{something}/{file}, drop the middle folder.
                        var p = st.SelectedFile!.Replace('\\', '/');
                        const string prefix = "assets/generated/";
                        if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var rest = p.Substring(prefix.Length);
                            var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                var repaired = prefix + parts[^1];
                                var repairedFull = store.ResolveDataPath(repaired);
                                if (System.IO.File.Exists(repairedFull))
                                {
                                    st = st with { SelectedFile = repaired };
                                    store.Set(st);
                                    store.SaveToDisk();
                                }
                                else
                                {
                                    // If we can't repair it, clear the selection to avoid boot 404 spam.
                                    st = st with { SelectedFile = null };
                                    store.Set(st);
                                    store.SaveToDisk();
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // If selection is invalid/unresolvable, clear it.
                    st = st with { SelectedFile = null };
                    store.Set(st);
                    store.SaveToDisk();
                }
            }
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
            store.Set(ProjectState.CreateDefault());
            
            // Clear assets (inputs and generated)
            if (Directory.Exists(store.InputsDir))
            {
                foreach (var file in Directory.GetFiles(store.InputsDir))
                    File.Delete(file);
            }
            if (Directory.Exists(store.GeneratedDir))
            {
                Directory.Delete(store.GeneratedDir, true);
                Directory.CreateDirectory(store.GeneratedDir);
            }
            
            store.SaveToDisk();
            return Results.Ok();
        });

        app.MapPost("/api/project/save", (ProjectStore store) =>
        {
            store.SaveToDisk();
            return Results.Ok();
        });

        app.MapPost("/api/assets/upload", async (ProjectStore store, HttpRequest req, CancellationToken ct) =>
        {
            if (!req.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data" });

            var form = await req.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file == null) return Results.BadRequest(new { error = "No file" });

            await using var s = file.OpenReadStream();
            var info = await store.SaveAssetAsync(file.FileName, s, ct);
            return Results.Json(info);
        });

        app.MapGet("/api/assets", (ProjectStore store) => Results.Json(store.ListAssets()));

        app.MapDelete("/api/assets/{name}", (ProjectStore store, string name) =>
        {
            return store.TryDeleteAsset(name) ? Results.Ok() : Results.NotFound();
        });

        app.MapGet("/api/files/tree", (FileTreeService tree, ILogger<FileTreeService> logger) =>
        {
            try
            {
                var result = tree.GetTree();
                logger.LogInformation("/api/files/tree: success");
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
                // Inspect first 512 bytes for nulls or control chars to guess
                var buffer = new byte[512];
                await using var fs = new FileStream(full, FileMode.Open, FileAccess.Read);
                var read = await fs.ReadAsync(buffer, 0, buffer.Length, ct);
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == 0) // Null byte is a strong indicator of binary
                    {
                        isBinary = true;
                        break;
                    }
                }
            }

            if (!isBinary)
            {
                var txt = await File.ReadAllTextAsync(full, Encoding.UTF8, ct);
                return Results.Json(new { path, kind = "text", text = txt, sizeBytes = fi.Length });
            }

            var bytes = await File.ReadAllBytesAsync(full, ct);
            return Results.Json(new { path, kind = "binary", base64 = Convert.ToBase64String(bytes), sizeBytes = fi.Length });
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

            // Safety: only allow deleting under assets/
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
                // Prefer selecting the first produced *.bin.json for quick inspection
                var firstLogic = result.Artifacts.Keys.OrderBy(k => k).FirstOrDefault();
                // Generated artifacts are written directly under assets/generated/ (no timestamp folder).
                var selectedFile = firstLogic != null
                    ? $"assets/generated/{firstLogic}.bin.json"
                    : null;
                store.Set(store.Get() with { LastBuildId = result.BuildId, SelectedFile = selectedFile });
                store.SaveToDisk();
                return Results.Json(new
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

        app.MapPost("/api/run", async (ProjectStore store, RuntimeSessionService runtime, CancellationToken ct) =>
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
                var snap = await runtime.RunAsync(state, state.SelectedAsset!, logicCs, ct);
                return Results.Json(snap);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        });

        app.MapPost("/api/stop", async (RuntimeSessionService runtime, CancellationToken ct) =>
        {
            await runtime.StopAsync(ct);
            return Results.Ok();
        });

        app.MapPost("/api/connectAll", () =>
        {
            // Pending: pre-connect nodes without starting code.
            return Results.Ok(new { ok = true, status = "Pending" });
        });

        app.MapPost("/api/command", async (TerminalBroadcaster term, HttpRequest req, CancellationToken ct) =>
        {
            var payload = await req.ReadFromJsonAsync<CommandRequest>(cancellationToken: ct);
            if (payload == null || string.IsNullOrWhiteSpace(payload.Command))
                return Results.BadRequest(new { error = "Missing command" });

            await term.LineAsync($"[cmd] Pending: {payload.Command}", ct);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/runtime", (RuntimeSessionService runtime) => Results.Json(runtime.GetSnapshot()));

        app.MapGet("/api/project/export", (ProjectStore store) =>
        {
            try
            {
                var zipPath = Path.Combine(Path.GetTempPath(), $"coralinker-export-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    // 1. Add project.json (node map)
                    if (File.Exists(store.ProjectFile))
                    {
                        zip.CreateEntryFromFile(store.ProjectFile, "project.json");
                    }

                    // 2. Add all user code from inputs
                    if (Directory.Exists(store.InputsDir))
                    {
                        foreach (var file in Directory.GetFiles(store.InputsDir, "*.*", SearchOption.AllDirectories))
                        {
                            var relativePath = Path.GetRelativePath(store.DataDir, file).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, relativePath);
                        }
                    }

                    // 3. Add all generated artifacts
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
    }
}

public sealed record CommandRequest(string Command);

public sealed record FileWriteRequest(string Path, string Kind, string? Text, string? Base64);

public sealed record FileDeleteRequest(string Path);

public sealed record NewInputRequest(string Name, string? Template);


