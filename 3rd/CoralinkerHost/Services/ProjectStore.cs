using System.Text.Json;

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
            var state = JsonSerializer.Deserialize<ProjectState>(json, ProjectJson.Options);
            if (state != null) Set(state);
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

public sealed record ProjectState(List<NodeState> Nodes, List<LinkState> Links, string? SelectedAsset, string? SelectedFile, string? LastBuildId)
{
    public static ProjectState CreateDefault()
    {
        var root = new NodeState(
            Id: "root",
            Title: "root",
            Kind: "root",
            X: 80,
            Y: 80,
            W: null,
            H: null,
            Properties: new Dictionary<string, string>
            {
                ["mcuUri"] = "PC",
                ["logicName"] = "Root"
            });

        return new ProjectState(new List<NodeState> { root }, new List<LinkState>(), null, null, null);
    }
}

public sealed record NodeState(
    string Id,
    string Title,
    string Kind,
    double X,
    double Y,
    double? W,
    double? H,
    Dictionary<string, string> Properties);

public sealed record LinkState(string Id, string FromNodeId, int? FromSlot, string ToNodeId, int? ToSlot);

internal static class ProjectJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}


