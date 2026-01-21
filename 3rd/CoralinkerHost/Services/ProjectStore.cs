using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoralinkerHost.Services;

public sealed class ProjectStore
{
    private readonly object _gate = new();
    private readonly IHostEnvironment _env;

    private ProjectState _state = ProjectState.CreateDefault();

    public ProjectStore(IHostEnvironment env)
    {
        _env = env;
        EnsureDataLayout();
        LoadFromDiskIfExists();
    }

    public string HostRoot => _env.ContentRootPath;
    public string DataDir => Path.Combine(_env.ContentRootPath, "data");
    public string AssetsDir => Path.Combine(DataDir, "assets");
    public string InputsDir => Path.Combine(AssetsDir, "inputs");
    public string GeneratedDir => Path.Combine(AssetsDir, "generated");
    public string BuildsDir => Path.Combine(DataDir, "builds");
    public string ProjectFile => Path.Combine(DataDir, "project.json");
    
    // Note: For customer deployment, data will be in executable directory.
    // In development, it's in ContentRootPath for convenience.

    public ProjectState Get()
    {
        lock (_gate) return _state;
    }

    public void Set(ProjectState state)
    {
        lock (_gate) _state = state;
    }

    public void SaveToDisk()
    {
        ProjectState snap;
        lock (_gate) snap = _state;
        var json = JsonSerializer.Serialize(snap, ProjectJson.Options);
        File.WriteAllText(ProjectFile, json);
    }

    public void LoadFromDiskIfExists()
    {
        if (!File.Exists(ProjectFile)) return;
        try
        {
            var json = File.ReadAllText(ProjectFile);
            
            // 尝试检测旧格式（nodeMap 是字符串）并迁移
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("nodeMap", out var nodeMapEl) && 
                nodeMapEl.ValueKind == JsonValueKind.String)
            {
                // 旧格式：nodeMap 是字符串，需要迁移
                var nodeMapStr = nodeMapEl.GetString();
                JsonNode? nodeMapObj = null;
                
                if (!string.IsNullOrWhiteSpace(nodeMapStr))
                {
                    nodeMapObj = JsonNode.Parse(nodeMapStr);
                }
                
                var state = new ProjectState(
                    nodeMapObj,
                    root.TryGetProperty("selectedAsset", out var sa) && sa.ValueKind == JsonValueKind.String ? sa.GetString() : null,
                    root.TryGetProperty("selectedFile", out var sf) && sf.ValueKind == JsonValueKind.String ? sf.GetString() : null,
                    root.TryGetProperty("lastBuildId", out var lb) && lb.ValueKind == JsonValueKind.String ? lb.GetString() : null
                );
                
                Set(state);
                
                // 自动迁移：以新格式保存
                SaveToDisk();
                return;
            }
            
            // 新格式：直接反序列化
            var newState = JsonSerializer.Deserialize<ProjectState>(json, ProjectJson.Options);
            if (newState != null) Set(newState);
        }
        catch
        {
            // ignore and keep defaults
        }
    }

    public IReadOnlyList<AssetInfo> ListAssets()
    {
        Directory.CreateDirectory(InputsDir);
        var files = Directory.GetFiles(InputsDir, "*.cs", SearchOption.TopDirectoryOnly);
        return files
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new AssetInfo(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();
    }

    public async Task<AssetInfo> SaveAssetAsync(string fileName, Stream content, CancellationToken ct)
    {
        if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .cs files are supported.");

        fileName = Path.GetFileName(fileName);
        Directory.CreateDirectory(InputsDir);
        var target = Path.Combine(InputsDir, fileName);
        await using (var fs = File.Create(target))
        {
            await content.CopyToAsync(fs, ct);
        }
        var fi = new FileInfo(target);
        return new AssetInfo(fi.Name, fi.Length, fi.LastWriteTimeUtc);
    }

    public bool TryDeleteAsset(string fileName)
    {
        fileName = Path.GetFileName(fileName);
        var target = Path.Combine(InputsDir, fileName);
        if (!File.Exists(target)) return false;
        File.Delete(target);
        return true;
    }

    public string ResolveDataPath(string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/').TrimStart('/');
        if (relativePath.Contains("..", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid path.");
        var full = Path.GetFullPath(Path.Combine(DataDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var dataFull = Path.GetFullPath(DataDir);
        if (!full.StartsWith(dataFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path outside data dir.");
        return full;
    }

    public void EnsureDataLayout()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(AssetsDir);
        Directory.CreateDirectory(InputsDir);
        Directory.CreateDirectory(GeneratedDir);
        Directory.CreateDirectory(BuildsDir);

        // Migration: older versions stored *.cs directly under data/assets
        foreach (var f in Directory.GetFiles(AssetsDir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(f);
            var dst = Path.Combine(InputsDir, name);
            if (!File.Exists(dst))
                File.Move(f, dst);
            else
                File.Delete(f);
        }
    }
}

public sealed record AssetInfo(string Name, long SizeBytes, DateTime LastWriteUtc);

/// <summary>
/// Project state - matches frontend ProjectState interface.
/// nodeMap stores the raw LiteGraph serialized JSON object (not string).
/// </summary>
public sealed record ProjectState(
    JsonNode? NodeMap,         // LiteGraph 序列化的 JSON 对象 (直接存储，非字符串)
    string? SelectedAsset,     // 当前选中的 .cs 资源文件名
    string? SelectedFile,      // 当前在编辑器中打开的文件路径
    string? LastBuildId        // 最后一次构建的 ID
)
{
    public static ProjectState CreateDefault()
    {
        // 创建默认的 LiteGraph 图数据，包含一个 Root 节点
        var defaultGraph = new JsonObject
        {
            ["last_node_id"] = 1,
            ["last_link_id"] = 0,
            ["nodes"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 1,
                    ["type"] = "coral/root",
                    ["pos"] = new JsonArray { 50, 50 },
                    ["size"] = new JsonArray { 200, 90 },
                    ["flags"] = new JsonObject(),
                    ["order"] = 0,
                    ["mode"] = 0,
                    ["outputs"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "out",
                            ["type"] = "flow",
                            ["links"] = null
                        }
                    },
                    ["properties"] = new JsonObject
                    {
                        ["name"] = "PC"
                    }
                }
            },
            ["links"] = new JsonArray(),
            ["groups"] = new JsonArray(),
            ["config"] = new JsonObject(),
            ["extra"] = new JsonObject(),
            ["version"] = 0.4
        };
        
        return new ProjectState(defaultGraph, null, null, null);
    }
}

internal static class ProjectJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}


