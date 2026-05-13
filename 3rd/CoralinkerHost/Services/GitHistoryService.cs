using System.Diagnostics;
using System.Text;

namespace CoralinkerHost.Services;

public sealed class GitHistoryService
{
    private const string InputsGitPath = "assets/inputs";
    private const string EmptyTreeHash = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
    private readonly ProjectStore _store;
    private readonly object _gate = new();

    public GitHistoryService(ProjectStore store)
    {
        _store = store;
        _store.EnsureDataLayout();
        EnsureRepository();
    }

    public GitStatusSnapshot GetStatus()
    {
        lock (_gate)
        {
            EnsureRepository();
            var head = GetHeadUnsafe();
            var dirtyFiles = GetDirtyFilesUnsafe();
            return new GitStatusSnapshot(
                head?.Hash,
                head?.ShortHash,
                head?.CommitTime,
                dirtyFiles.Length > 0,
                dirtyFiles
            );
        }
    }

    public GitCommitResult CommitInputsIfChanged(string reason)
    {
        lock (_gate)
        {
            EnsureRepository();
            var before = GetHeadUnsafe();
            RunGit("add -- assets/inputs");

            if (!HasStagedChangesUnsafe())
            {
                return new GitCommitResult(before?.Hash, before?.Hash, false);
            }

            var message = string.IsNullOrWhiteSpace(reason)
                ? $"save {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                : reason;
            RunGit(["commit", "-m", message]);
            var after = GetHeadUnsafe();
            return new GitCommitResult(before?.Hash, after?.Hash, true);
        }
    }

    public GitCommitListResult GetLog(string? relativePath, int maxCount = 100)
    {
        lock (_gate)
        {
            EnsureRepository();
            var path = NormalizeOptionalInputPath(relativePath);
            if (GetHeadUnsafe() == null)
            {
                return new GitCommitListResult(Array.Empty<GitCommitInfo>());
            }

            var args = new List<string>
            {
                "log",
                $"-n{Math.Clamp(maxCount, 1, 500)}",
                "--date=iso-strict",
                "--pretty=format:%H%x1f%h%x1f%cI%x1f%an%x1f%s"
            };
            if (path != null)
            {
                args.Add("--");
                args.Add(path);
            }

            var output = RunGit(args.ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(output))
            {
                return new GitCommitListResult(Array.Empty<GitCommitInfo>());
            }

            var commits = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseCommitLine)
                .Where(c => c != null)
                .Select(c => c!)
                .Select(c => c with { Files = GetCommitFilesUnsafe(c.Hash) })
                .ToArray();
            return new GitCommitListResult(commits);
        }
    }

    public GitDiffResult GetDiff(string? from, string? to, string? relativePath)
    {
        lock (_gate)
        {
            EnsureRepository();
            var path = NormalizeOptionalInputPath(relativePath);
            var fromRev = NormalizeRevision(from, "HEAD~1");
            var toRev = NormalizeRevision(to, "HEAD");
            fromRev = ResolveRevisionOrEmptyTreeUnsafe(fromRev);

            var args = new List<string> { "diff", "--no-ext-diff", "--", };
            if (path == null)
            {
                args = ["diff", "--no-ext-diff", fromRev, toRev, "--", InputsGitPath];
            }
            else
            {
                args = ["diff", "--no-ext-diff", fromRev, toRev, "--", path];
            }

            var unified = RunGitAllowFailure(args.ToArray(), out var diffExit);
            if (diffExit != 0 && diffExit != 1)
            {
                throw new InvalidOperationException(unified);
            }

            string? oldText = null;
            string? newText = null;
            if (path != null)
            {
                oldText = ReadFileAtRevisionUnsafe(fromRev, path);
                newText = ReadFileAtRevisionUnsafe(toRev, path);
            }

            return new GitDiffResult(fromRev, toRev, path, unified, oldText, newText);
        }
    }

    public GitFileAtCommitResult GetFile(string commit, string relativePath)
    {
        lock (_gate)
        {
            EnsureRepository();
            var path = NormalizeInputPath(relativePath);
            var revision = NormalizeRevision(commit, "HEAD");
            return new GitFileAtCommitResult(revision, path, ReadFileAtRevisionUnsafe(revision, path) ?? "");
        }
    }

    public GitCheckoutResult Checkout(string commit, string? relativePath)
    {
        lock (_gate)
        {
            EnsureRepository();
            var revision = NormalizeRevision(commit, "HEAD");
            var path = NormalizeOptionalInputPath(relativePath);
            if (path == null)
            {
                RunGit(["checkout", revision, "--", InputsGitPath]);
            }
            else
            {
                RunGit(["checkout", revision, "--", path]);
            }

            var status = GetStatus();
            return new GitCheckoutResult(status.Head, status.ShortHead, status.DirtyFiles);
        }
    }

    public GitCommitResult RevertAsCurrent(string commit, string? relativePath)
    {
        lock (_gate)
        {
            EnsureRepository();
            var before = GetHeadUnsafe();
            var revision = NormalizeRevision(commit, "HEAD");
            var path = NormalizeOptionalInputPath(relativePath);

            if (path == null)
            {
                RunGit(["checkout", revision, "--", InputsGitPath]);
            }
            else
            {
                RunGit(["checkout", revision, "--", path]);
            }

            RunGit("add -- assets/inputs");
            if (!HasStagedChangesUnsafe())
            {
                return new GitCommitResult(before?.Hash, before?.Hash, false);
            }

            RunGit(["commit", "-m", $"revert to {revision} {DateTime.Now:yyyy-MM-dd HH:mm:ss}"]);
            var after = GetHeadUnsafe();
            return new GitCommitResult(before?.Hash, after?.Hash, true);
        }
    }

    public GitHeadInfo? GetHead()
    {
        lock (_gate)
        {
            EnsureRepository();
            return GetHeadUnsafe();
        }
    }

    public bool HasDirtyInputs()
    {
        lock (_gate)
        {
            EnsureRepository();
            return GetDirtyFilesUnsafe().Length > 0;
        }
    }

    private void EnsureRepository()
    {
        Directory.CreateDirectory(_store.DataDir);
        Directory.CreateDirectory(_store.InputsDir);

        if (!Directory.Exists(Path.Combine(_store.DataDir, ".git")))
        {
            RunGit("init");
        }

        var gitignorePath = Path.Combine(_store.DataDir, ".gitignore");
        if (!File.Exists(gitignorePath))
        {
            File.WriteAllText(
                gitignorePath,
                "*\n!assets/\n!assets/inputs/\n!assets/inputs/**/*.cs\n!.gitignore\n",
                Encoding.UTF8
            );
        }
    }

    private GitHeadInfo? GetHeadUnsafe()
    {
        var hash = RunGitAllowFailure(["rev-parse", "--verify", "HEAD"], out var exit).Trim();
        if (exit != 0 || string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        var line = RunGit(["show", "-s", "--date=iso-strict", "--pretty=format:%H%x1f%h%x1f%cI", "HEAD"]).Trim();
        var parts = line.Split('\u001f');
        if (parts.Length < 3)
        {
            return new GitHeadInfo(hash, hash[..Math.Min(7, hash.Length)], null);
        }

        return new GitHeadInfo(parts[0], parts[1], DateTimeOffset.TryParse(parts[2], out var t) ? t : null);
    }

    private string[] GetDirtyFilesUnsafe()
    {
        var output = RunGitAllowFailure(["status", "--porcelain", "--", InputsGitPath], out _);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length > 3 ? line[3..].Trim().Replace('\\', '/') : "")
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private bool HasStagedChangesUnsafe()
    {
        RunGitAllowFailure(["diff", "--cached", "--quiet", "--", InputsGitPath], out var exit);
        return exit == 1;
    }

    private string[] GetCommitFilesUnsafe(string commit)
    {
        var output = RunGitAllowFailure(["diff-tree", "--root", "--no-commit-id", "--name-only", "-r", commit, "--", InputsGitPath], out _);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().Replace('\\', '/'))
            .ToArray();
    }

    private static GitCommitInfo? ParseCommitLine(string line)
    {
        var parts = line.TrimEnd('\r').Split('\u001f');
        if (parts.Length < 5) return null;
        return new GitCommitInfo(
            parts[0],
            parts[1],
            DateTimeOffset.TryParse(parts[2], out var t) ? t : null,
            parts[3],
            parts[4],
            Array.Empty<string>()
        );
    }

    private string? ReadFileAtRevisionUnsafe(string revision, string path)
    {
        if (revision == EmptyTreeHash)
        {
            return null;
        }
        var output = RunGitAllowFailure(["show", $"{revision}:{path}"], out var exit);
        return exit == 0 ? output : null;
    }

    private string ResolveRevisionOrEmptyTreeUnsafe(string revision)
    {
        RunGitAllowFailure(["rev-parse", "--verify", $"{revision}^{{commit}}"], out var exit);
        return exit == 0 ? revision : EmptyTreeHash;
    }

    private static string NormalizeRevision(string? revision, string fallback)
    {
        revision = string.IsNullOrWhiteSpace(revision) ? fallback : revision.Trim();
        if (revision.Contains(' ') || revision.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid git revision.");
        }
        return revision;
    }

    private string? NormalizeOptionalInputPath(string? relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath) ? null : NormalizeInputPath(relativePath);
    }

    private string NormalizeInputPath(string relativePath)
    {
        var path = relativePath.Replace('\\', '/').TrimStart('/');
        if (path.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
        {
            path = path["data/".Length..];
        }

        if (path.Contains("..", StringComparison.Ordinal) ||
            !path.StartsWith("assets/inputs/", StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path must be under assets/inputs and end with .cs.");
        }

        return path;
    }

    private string RunGit(string args) => RunGit(SplitArgs(args));

    private string RunGit(params string[] args)
    {
        var output = RunGitAllowFailure(args, out var exitCode);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(output);
        }
        return output;
    }

    private string RunGitAllowFailure(string[] args, out int exitCode)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _store.DataDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["GIT_AUTHOR_NAME"] = "CoralinkerHost";
        psi.Environment["GIT_AUTHOR_EMAIL"] = "coralinker@localhost";
        psi.Environment["GIT_COMMITTER_NAME"] = "CoralinkerHost";
        psi.Environment["GIT_COMMITTER_EMAIL"] = "coralinker@localhost";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        exitCode = proc.ExitCode;
        return stdout + stderr;
    }

    private static string[] SplitArgs(string args)
    {
        return args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}

public sealed record GitHeadInfo(string Hash, string ShortHash, DateTimeOffset? CommitTime);
public sealed record GitStatusSnapshot(string? Head, string? ShortHead, DateTimeOffset? CommitTime, bool IsDirty, string[] DirtyFiles);
public sealed record GitCommitResult(string? HeadBefore, string? HeadAfter, bool Committed);
public sealed record GitCommitInfo(string Hash, string ShortHash, DateTimeOffset? CommitTime, string Author, string Subject, string[] Files);
public sealed record GitCommitListResult(GitCommitInfo[] Commits);
public sealed record GitDiffResult(string From, string To, string? Path, string UnifiedDiff, string? OldText, string? NewText);
public sealed record GitFileAtCommitResult(string Commit, string Path, string Text);
public sealed record GitCheckoutResult(string? Head, string? ShortHead, string[] DirtyFiles);
