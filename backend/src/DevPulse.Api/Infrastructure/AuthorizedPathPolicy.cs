using DevPulse.Api.Application;

namespace DevPulse.Api.Infrastructure;

public sealed class RepositoryOptions
{
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.Ordinal);
    public int TimeoutMilliseconds { get; set; } = 5000;
    public int OutputLimitBytes { get; set; } = 2097152;
    public string[] AuthorizedRoots { get; set; } = [];
    public string GitExecutable { get; set; } = OperatingSystem.IsWindows() ? @"C:\Program Files\Git\cmd\git.exe" : "/usr/bin/git";
}

public sealed class AuthorizedPathPolicy(RepositoryOptions options) : IAuthorizedPathPolicy
{
    private static readonly StringComparison Comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public AuthorizedRepository Authorize(string? path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
                path.IndexOf('\0') >= 0 || HasTraversal(path))
                throw Invalid();
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var root = options.AuthorizedRoots.Where(IsValidRoot).Select(p =>
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
                .FirstOrDefault(r => IsWithin(full, r));
            if (root is null) throw Denied();
            // Walk from the filesystem root: do not silently authorize linked roots or ancestors.
            VerifyDirectoryChain(root);
            VerifyDirectoryChain(full);
            if (!Directory.Exists(full)) throw Invalid();
            var metadata = Path.Combine(full, ".git");
            if (File.Exists(metadata)) throw Unsupported();
            if (!Directory.Exists(metadata))
                throw new RepositoryFailure(400, "not_repository", "Choose a Git repository root containing a .git directory.");
            InspectMetadata(metadata, cancellationToken);
            return new(full, Path.GetFileName(full));
        }
        catch (RepositoryFailure) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw Denied();
        }
    }

    private static bool HasTraversal(string path) => path.Split(
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
        .Any(p => p is ".." or ".");

    private static bool IsValidRoot(string path) => !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) && !HasTraversal(path);

    public static bool IsWithin(string path, string root) => string.Equals(path, root, Comparison) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, Comparison);

    private static void VerifyDirectoryChain(string path)
    {
        var anchor = Path.GetPathRoot(path) ?? throw Denied();
        var current = anchor;
        RejectLink(current);
        foreach (var part in path[anchor.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current)) throw Invalid();
            RejectLink(current);
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Denied();
    }

    private static void InspectMetadata(string metadata, CancellationToken cancellationToken)
    {
        // Deliberately conservative M01 layout: no linked worktrees or external object stores.
        var forbidden = new[] { "commondir", "gitdir", "config.worktree", "objects/info/alternates", "objects/info/http-alternates" };
        foreach (var name in forbidden)
        {
            var candidate = Path.Combine(metadata, name.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate) || Directory.Exists(candidate)) throw Unsupported();
        }
        var pending = new Stack<string>();
        pending.Push(metadata);
        var count = 0;
        var deadline = Environment.TickCount64 + 2000;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > 100000 || Environment.TickCount64 > deadline) throw Unsupported();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw Denied();
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
        var config = Path.Combine(metadata, "config");
        if (!File.Exists(config) || new FileInfo(config).Length > 65536) throw Unsupported();
        // Reject indirection at config level, including conditional includes and worktree overrides.
        // Escaped/continued config is outside the deliberately restricted M01 layout.
        foreach (var line in File.ReadLines(config))
        {
            var text = line.Trim();
            if (text.StartsWith('#') || text.StartsWith(';') || text.Length == 0) continue;
            if (text.Contains('\\') || text.StartsWith("[include", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("[extensions", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("worktree", StringComparison.OrdinalIgnoreCase)) throw Unsupported();
        }
    }

    private static RepositoryFailure Invalid() => new(400, "invalid_path", "Provide an existing absolute repository-root directory without traversal components.");
    private static RepositoryFailure Denied() => new(403, "path_not_authorized", "The repository path or metadata is outside the configured safe boundary.");
    private static RepositoryFailure Unsupported() => new(422, "unsupported_repository_layout", "This repository layout is not supported. Use an ordinary repository with self-contained Git metadata.");
}
