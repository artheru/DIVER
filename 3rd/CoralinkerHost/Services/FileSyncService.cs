using System.Security.Cryptography;
using System.Text;

namespace CoralinkerHost.Services;

public sealed class FileSyncService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".json", ".xml", ".txt", ".md", ".html", ".css", ".js", ".map"
    };

    private readonly ProjectStore _store;
    private readonly GitHistoryService _history;
    private readonly DiverBuildService _builder;

    public FileSyncService(ProjectStore store, GitHistoryService history, DiverBuildService builder)
    {
        _store = store;
        _history = history;
        _builder = builder;
    }

    public FileSnapshotResult GetSnapshot()
    {
        _store.EnsureDataLayout();
        var status = _history.GetStatus();
        var files = Directory.Exists(_store.AssetsDir)
            ? Directory.GetFiles(_store.AssetsDir, "*", SearchOption.AllDirectories)
                .Where(path => !IsGitInternal(path))
                .Select(BuildSnapshotFile)
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<FileSnapshotItem>();

        return new FileSnapshotResult(status.Head, status.ShortHead, status.CommitTime, status.IsDirty, status.DirtyFiles, files);
    }

    public async Task<FileSyncResult> SyncAsync(FileSyncRequest request, CancellationToken ct)
    {
        if (_builder.IsBuilding)
        {
            throw new FileSyncConflictException("Build is running; editing is temporarily locked.", Array.Empty<FileSyncConflict>());
        }
        if (request.Changes == null || request.Changes.Length == 0)
        {
            var status = _history.GetStatus();
            return new FileSyncResult(true, status.Head, status.Head, false, Array.Empty<FileSyncChangeResult>(), Array.Empty<FileSyncConflict>());
        }

        var statusBefore = _history.GetStatus();
        var conflicts = new List<FileSyncConflict>();
        if (!request.Force &&
            !string.IsNullOrWhiteSpace(request.BaseHead) &&
            !string.Equals(request.BaseHead, statusBefore.Head, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var change in request.Changes)
            {
                var normalized = NormalizeInputPath(change.Path);
                var currentHash = TryHashPath(normalized);
                if (!string.Equals(currentHash, change.BaseHash, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add(new FileSyncConflict(normalized, change.BaseHash, currentHash, "Remote file changed since baseHead."));
                }
            }
        }
        if (conflicts.Count > 0)
        {
            throw new FileSyncConflictException("Remote HEAD changed. Refresh, merge, then retry.", conflicts.ToArray());
        }

        var results = new List<FileSyncChangeResult>();
        foreach (var change in request.Changes)
        {
            var path = NormalizeInputPath(change.Path);
            var full = _store.ResolveDataPath(path);
            var beforeHash = File.Exists(full) ? HashFile(full) : null;

            if (string.Equals(change.Action, "delete", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(full))
                {
                    File.Delete(full);
                }
                results.Add(new FileSyncChangeResult(path, "delete", beforeHash, null, File.Exists(full)));
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (string.Equals(change.Kind, "binary", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(change.Base64))
                {
                    throw new InvalidOperationException($"Missing base64 for {path}.");
                }
                await File.WriteAllBytesAsync(full, Convert.FromBase64String(change.Base64), ct);
            }
            else
            {
                await File.WriteAllTextAsync(full, change.Text ?? "", Encoding.UTF8, ct);
            }

            var afterHash = HashFile(full);
            results.Add(new FileSyncChangeResult(path, "write", beforeHash, afterHash, File.Exists(full)));
        }

        var message = string.IsNullOrWhiteSpace(request.CommitMessage)
            ? $"agent sync {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            : request.CommitMessage.Trim();
        var commit = _history.CommitInputsIfChanged(message);
        return new FileSyncResult(true, commit.HeadBefore, commit.HeadAfter, commit.Committed, results.ToArray(), Array.Empty<FileSyncConflict>());
    }

    private FileSnapshotItem BuildSnapshotFile(string fullPath)
    {
        var relative = Path.GetRelativePath(_store.DataDir, fullPath).Replace('\\', '/');
        var info = new FileInfo(fullPath);
        var kind = TextExtensions.Contains(info.Extension) ? "text" : "binary";
        return new FileSnapshotItem(relative, kind, info.Length, info.LastWriteTimeUtc, HashFile(fullPath));
    }

    private string NormalizeInputPath(string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');
        if (path.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
        {
            path = path["data/".Length..];
        }
        if (path.Contains("..", StringComparison.Ordinal) ||
            !path.StartsWith("assets/inputs/", StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Agent sync paths must be under assets/inputs and end with .cs.");
        }
        return path;
    }

    private string? TryHashPath(string relativePath)
    {
        var full = _store.ResolveDataPath(relativePath);
        return File.Exists(full) ? HashFile(full) : null;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsGitInternal(string path)
    {
        return path.Replace('\\', '/').Contains("/.git/", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record FileSnapshotResult(string? Head, string? ShortHead, DateTimeOffset? CommitTime, bool IsDirty, string[] DirtyFiles, FileSnapshotItem[] Files);
public sealed record FileSnapshotItem(string Path, string Kind, long SizeBytes, DateTime LastModifiedUtc, string Sha256);
public sealed record FileSyncRequest(string? BaseHead, string? CommitMessage, bool Force, FileSyncChange[] Changes);
public sealed record FileSyncChange(string Path, string Action, string? Kind, string? Text, string? Base64, string? BaseHash);
public sealed record FileSyncChangeResult(string Path, string Action, string? BeforeHash, string? AfterHash, bool Exists);
public sealed record FileSyncResult(bool Ok, string? HeadBefore, string? HeadAfter, bool Committed, FileSyncChangeResult[] Changes, FileSyncConflict[] Conflicts);
public sealed record FileSyncConflict(string Path, string? BaseHash, string? CurrentHash, string Reason);

public sealed class FileSyncConflictException : Exception
{
    public FileSyncConflictException(string message, FileSyncConflict[] conflicts) : base(message)
    {
        Conflicts = conflicts;
    }

    public FileSyncConflict[] Conflicts { get; }
}
