using System.Text.Json;
using System.Text.Json.Nodes;
using CoralinkerSDK;

namespace CoralinkerHost.Services;

/// <summary>
/// 项目存储服务
/// 管理项目文件和资源，节点数据统一由 DIVERSession 管理
/// </summary>
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

    public ProjectState Get()
    {
        lock (_gate) return _state;
    }

    public void Set(ProjectState state)
    {
        lock (_gate) _state = state;
    }

    /// <summary>
    /// 保存项目到磁盘（包括 DIVERSession 的节点数据）
    /// </summary>
    public void SaveToDisk()
    {
        // 从 DIVERSession 导出节点数据
        var nodes = DIVERSession.Instance.ExportNodes();
        
        // 调试日志：显示每个节点的 LogicName
        Console.WriteLine($"[SaveToDisk] Exporting {nodes.Count} node(s):");
        foreach (var kv in nodes)
        {
            Console.WriteLine($"  [{kv.Key}] NodeName={kv.Value.NodeName}, LogicName={kv.Value.LogicName ?? "null"}, HasProgram={kv.Value.ProgramBase64 != null}");
        }
        
        ProjectState snap;
        lock (_gate) snap = _state;
        
        // 构建完整的项目数据
        var projectData = new JsonObject
        {
            ["selectedAsset"] = snap.SelectedAsset,
            ["selectedFile"] = snap.SelectedFile,
            ["lastBuildId"] = snap.LastBuildId,
            ["nodes"] = JsonSerializer.SerializeToNode(nodes, ProjectJson.Options),
            ["controlLayout"] = snap.ControlLayout != null 
                ? JsonSerializer.SerializeToNode(snap.ControlLayout, ProjectJson.Options) 
                : null
        };
        
        var json = projectData.ToJsonString(ProjectJson.Options);
        Console.WriteLine($"[SaveToDisk] Writing to {ProjectFile}");
        File.WriteAllText(ProjectFile, json);
    }

    /// <summary>
    /// 从磁盘加载项目（包括恢复 DIVERSession 的节点数据）
    /// </summary>
    public void LoadFromDiskIfExists()
    {
        if (!File.Exists(ProjectFile)) return;
        
        try
        {
            var json = File.ReadAllText(ProjectFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // 读取基本属性
            ControlLayoutConfig? controlLayout = null;
            if (root.TryGetProperty("controlLayout", out var cl) && cl.ValueKind == JsonValueKind.Object)
            {
                controlLayout = JsonSerializer.Deserialize<ControlLayoutConfig>(cl.GetRawText(), ProjectJson.Options);
            }
            
            var state = new ProjectState(
                root.TryGetProperty("selectedAsset", out var sa) && sa.ValueKind == JsonValueKind.String ? sa.GetString() : null,
                root.TryGetProperty("selectedFile", out var sf) && sf.ValueKind == JsonValueKind.String ? sf.GetString() : null,
                root.TryGetProperty("lastBuildId", out var lb) && lb.ValueKind == JsonValueKind.String ? lb.GetString() : null,
                controlLayout
            );
            
            Set(state);
            
            // 加载节点数据到 DIVERSession
            if (root.TryGetProperty("nodes", out var nodesEl) && nodesEl.ValueKind == JsonValueKind.Object)
            {
                var nodesDict = JsonSerializer.Deserialize<Dictionary<string, NodeExportData>>(
                    nodesEl.GetRawText(), 
                    ProjectJson.Options
                );
                
                if (nodesDict != null)
                {
                    DIVERSession.Instance.ImportNodes(nodesDict);
                }
            }
            
            // 兼容旧格式：如果有 nodeMap，尝试迁移
            MigrateLegacyNodeMap(root);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProjectStore] Load error: {ex.Message}");
            // ignore and keep defaults
        }
    }

    /// <summary>
    /// 迁移旧格式的 nodeMap（LiteGraph/VueFlow 格式）
    /// </summary>
    private void MigrateLegacyNodeMap(JsonElement root)
    {
        if (!root.TryGetProperty("nodeMap", out var nodeMapEl)) return;
        
        try
        {
            JsonElement? nodesArray = null;
            
            if (nodeMapEl.ValueKind == JsonValueKind.String)
            {
                // 旧格式：nodeMap 是字符串
                var nodeMapStr = nodeMapEl.GetString();
                if (!string.IsNullOrWhiteSpace(nodeMapStr))
                {
                    using var nodeMapDoc = JsonDocument.Parse(nodeMapStr);
                    if (nodeMapDoc.RootElement.TryGetProperty("nodes", out var na))
                    {
                        nodesArray = na;
                    }
                }
            }
            else if (nodeMapEl.ValueKind == JsonValueKind.Object)
            {
                // 新格式：nodeMap 是对象
                if (nodeMapEl.TryGetProperty("nodes", out var na))
                {
                    nodesArray = na;
                }
            }
            
            if (nodesArray?.ValueKind != JsonValueKind.Array) return;
            
            // 从旧格式中提取节点信息（只提取位置等 ExtraInfo）
            // 注意：实际的节点连接需要用户重新 Probe
            Console.WriteLine("[ProjectStore] Found legacy nodeMap format, migration info extracted");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProjectStore] Migration error: {ex.Message}");
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

    /// <summary>
    /// 创建新项目（清空所有数据）
    /// </summary>
    public void CreateNew()
    {
        // 清空 DIVERSession
        DIVERSession.Instance.RemoveAllNodes();
        
        // 清空资源文件
        if (Directory.Exists(InputsDir))
        {
            foreach (var file in Directory.GetFiles(InputsDir))
                File.Delete(file);
        }
        if (Directory.Exists(GeneratedDir))
        {
            Directory.Delete(GeneratedDir, true);
            Directory.CreateDirectory(GeneratedDir);
        }
        
        // 重置状态
        Set(ProjectState.CreateDefault());
        SaveToDisk();
    }
}

public sealed record AssetInfo(string Name, long SizeBytes, DateTime LastWriteUtc);

/// <summary>
/// 项目状态 - 简化版，节点数据由 DIVERSession 管理
/// </summary>
public sealed record ProjectState(
    string? SelectedAsset,     // 当前选中的 .cs 资源文件名
    string? SelectedFile,      // 当前在编辑器中打开的文件路径
    string? LastBuildId,       // 最后一次构建的 ID
    ControlLayoutConfig? ControlLayout = null  // 遥控器布局配置
)
{
    public static ProjectState CreateDefault()
    {
        return new ProjectState(null, null, null, null);
    }
}

/// <summary>
/// 遥控器布局配置
/// </summary>
public sealed record ControlLayoutConfig(
    int WindowX,
    int WindowY,
    int GridCols,
    int GridRows,
    bool IsLocked,
    List<ControlWidget>? Widgets
);

/// <summary>
/// 遥控器控件
/// </summary>
public sealed record ControlWidget(
    string Id,
    string Type,
    int GridX,
    int GridY,
    int GridW,
    int GridH,
    JsonElement? Config
);

internal static class ProjectJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
