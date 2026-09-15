using System.Text.Json;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Domain.Rules;
using FractalMemory.Core.Infrastructure.Files;

namespace FractalMemory.Core.Infrastructure.Indexing;

public interface INodeListingCacheReader
{
    Task<IReadOnlyList<MemoryNode>?> TryLoadAsync(string repositoryRoot, string fingerprint, CancellationToken cancellationToken);
}

public sealed class NodeListingCacheReader(
    IRepositoryService repositoryService,
    IFileSystemService fileSystemService) : INodeListingCacheReader
{
    public async Task<IReadOnlyList<MemoryNode>?> TryLoadAsync(string repositoryRoot, string fingerprint, CancellationToken cancellationToken)
    {
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var cacheRoot = RepositoryPathGuard.ResolveContainedPath(storageRoot, "indexes/cache");
        var manifestPath = RepositoryPathGuard.ResolveContainedPath(cacheRoot, "manifest.json");
        if (!fileSystemService.FileExists(manifestPath))
        {
            return null;
        }

        IndexCacheManifest? manifest;
        try
        {
            var json = await fileSystemService.ReadAllTextAsync(manifestPath, cancellationToken);
            manifest = JsonSerializer.Deserialize<IndexCacheManifest>(json, IndexService.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is null || !string.Equals(manifest.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        var nodeCacheRoot = RepositoryPathGuard.ResolveContainedPath(cacheRoot, "nodes");
        var nodes = new List<MemoryNode>(manifest.Nodes.Count);
        foreach (var (relativePath, entry) in manifest.Nodes)
        {
            string normalizedPath;
            try
            {
                normalizedPath = NodePathRules.Normalize(relativePath);
                if (!string.Equals(normalizedPath, relativePath, StringComparison.Ordinal))
                {
                    return null;
                }

                RepositoryPathGuard.ResolveContainedPath(storageRoot, normalizedPath);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return null;
            }

            if (!IsSafeCacheFileName(entry.CacheFile))
            {
                return null;
            }

            var cacheFilePath = RepositoryPathGuard.ResolveContainedPath(nodeCacheRoot, entry.CacheFile);
            if (!IsContainedIn(nodeCacheRoot, cacheFilePath))
            {
                return null;
            }

            if (!fileSystemService.FileExists(cacheFilePath))
            {
                return null;
            }

            NodeCacheDocument? document;
            try
            {
                var json = await fileSystemService.ReadAllTextAsync(cacheFilePath, cancellationToken);
                document = JsonSerializer.Deserialize<NodeCacheDocument>(json, IndexService.JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }

            if (document is null)
            {
                return null;
            }

            if (!string.Equals(document.RelativePath, normalizedPath, StringComparison.Ordinal))
            {
                return null;
            }

            var fullPath = RepositoryPathGuard.ResolveContainedPath(storageRoot, normalizedPath);
            nodes.Add(new MemoryNode
            {
                RelativePath = normalizedPath,
                FullPath = fullPath,
                Metadata = document.Metadata,
                IndexFileName = document.IndexFileName,
                StateFileName = document.StateFileName,
                TimelineFileName = document.TimelineFileName,
                DecisionsFileName = document.DecisionsFileName,
                IndexContent = document.Files.TryGetValue(document.IndexFileName, out var idx) ? idx.RawContent : string.Empty,
                StateContent = document.Files.TryGetValue(document.StateFileName, out var st) ? st.RawContent : string.Empty,
                TimelineContent = document.Files.TryGetValue(document.TimelineFileName, out var tl) ? tl.RawContent : string.Empty,
                DecisionsContent = document.Files.TryGetValue(document.DecisionsFileName, out var dec) ? dec.RawContent : string.Empty,
            });
        }

        return nodes.OrderBy(node => node.RelativePath, StringComparer.Ordinal).ToArray();
    }

    internal static bool IsSafeCacheFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (Path.GetFileName(fileName) != fileName)
        {
            return false;
        }

        if (fileName.Contains("..", StringComparison.Ordinal) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains('/', StringComparison.Ordinal) ||
            fileName.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContainedIn(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }
}
