namespace CoralinkerHost.Services;

public sealed class FileTreeService
{
    private readonly ProjectStore _store;
    private readonly ILogger<FileTreeService> _logger;

    public FileTreeService(ProjectStore store, ILogger<FileTreeService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public FileNode GetTree()
    {
        try
        {
            _store.EnsureDataLayout();

            var root = new FileNode("assets", "assets", IsDir: true, SizeBytes: null, Children: new List<FileNode>());
            root.Children!.Add(DirNode("inputs", _store.InputsDir, "assets/inputs"));
            root.Children!.Add(DirNode("generated", _store.GeneratedDir, "assets/generated"));
            
            var totalFiles = CountFiles(root);
            _logger.LogInformation("GetTree: returning tree with {FileCount} total files", totalFiles);
            
            return root;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTree failed");
            throw;
        }
    }

    private static int CountFiles(FileNode node)
    {
        if (!node.IsDir) return 1;
        return (node.Children ?? new List<FileNode>()).Sum(CountFiles);
    }

    private FileNode DirNode(string name, string fullPath, string relPath)
    {
        try
        {
            Directory.CreateDirectory(fullPath);
            var children = new List<FileNode>();

            foreach (var d in Directory.GetDirectories(fullPath))
            {
                var dn = new DirectoryInfo(d);
                children.Add(DirNode(dn.Name, dn.FullName, $"{relPath}/{dn.Name}"));
            }

            foreach (var f in Directory.GetFiles(fullPath))
            {
                var fi = new FileInfo(f);
                children.Add(new FileNode(fi.Name, $"{relPath}/{fi.Name}", IsDir: false, SizeBytes: fi.Length, Children: null));
            }

            children = children
                .OrderByDescending(c => c.IsDir)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogDebug("DirNode {RelPath}: {DirCount} dirs, {FileCount} files", 
                relPath, 
                children.Count(c => c.IsDir), 
                children.Count(c => !c.IsDir));

            return new FileNode(name, relPath, IsDir: true, SizeBytes: null, Children: children);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DirNode failed for {FullPath} ({RelPath})", fullPath, relPath);
            throw;
        }
    }
}

public sealed record FileNode(string Name, string Path, bool IsDir, long? SizeBytes, List<FileNode>? Children);


