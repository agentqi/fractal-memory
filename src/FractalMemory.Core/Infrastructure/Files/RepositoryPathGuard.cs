using FractalMemory.Core.Application.Services;

namespace FractalMemory.Core.Infrastructure.Files;

public static class RepositoryPathGuard
{
    public static string CreateContainedDirectory(string rootPath, string relativePath, IFileSystemService fileSystem)
    {
        var path = ResolveContainedPath(rootPath, relativePath);
        fileSystem.CreateDirectory(path);
        // Creation can race with changes to ancestors; validate again before using the directory.
        EnsureContainedPath(rootPath, path);
        return path;
    }

    public static string ResolveContainedPath(string rootPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Repository storage paths must be relative.");
        }

        var normalizedRoot = NormalizeRoot(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));

        EnsureLexicallyContained(normalizedRoot, candidate);
        EnsureNoReparsePoints(normalizedRoot, candidate);
        return candidate;
    }

    public static void EnsureContainedPath(string rootPath, string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

        var normalizedRoot = NormalizeRoot(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        EnsureLexicallyContained(normalizedRoot, candidate);
        EnsureNoReparsePoints(normalizedRoot, candidate);
    }

    public static string NormalizeRelativeDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path))
        {
            throw new InvalidOperationException("Configured storage directories must be relative.");
        }

        var normalized = path.Trim().Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("Configured storage directories cannot be empty or contain relative traversal.");
        }

        foreach (var segment in segments)
        {
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException($"Configured storage directory contains an invalid segment '{segment}'.");
            }
        }

        return string.Join('/', segments);
    }

    public static IEnumerable<string> EnumerateReparsePoints(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootPath));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (IOException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    yield return entry;
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static string NormalizeRoot(string rootPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

    private static void EnsureLexicallyContained(string normalizedRoot, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var requiredPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!candidate.Equals(normalizedRoot, comparison) && !candidate.StartsWith(requiredPrefix, comparison))
        {
            throw new InvalidOperationException("Requested path resolves outside the FractalMem storage directory.");
        }
    }

    private static void EnsureNoReparsePoints(string normalizedRoot, string candidate)
    {
        var relative = Path.GetRelativePath(normalizedRoot, candidate);
        if (relative == ".")
        {
            return;
        }

        var current = normalizedRoot;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Symbolic links and reparse points are not allowed inside FractalMem storage paths.");
            }
        }
    }
}
