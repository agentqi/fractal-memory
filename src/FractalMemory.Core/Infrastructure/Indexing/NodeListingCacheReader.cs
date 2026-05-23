using System.Text.Json;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Infrastructure.Indexing;

public interface INodeListingCacheReader
{
    Task<IReadOnlyList<MemoryNode>?> TryLoadAsync(string repositoryRoot, DateTimeOffset fingerprint, CancellationToken cancellationToken);
}

public sealed class NodeListingCacheReader(
    IRepositoryService repositoryService,
    IFileSystemService fileSystemService) : INodeListingCacheReader
{
    public async Task<IReadOnlyList<MemoryNode>?> TryLoadAsync(string repositoryRoot, DateTimeOffset fingerprint, CancellationToken cancellationToken)
    {
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var cacheRoot = Path.Combine(storageRoot, "indexes", "cache");
        var manifestPath = Path.Combine(cacheRoot, "manifest.json");
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

        if (manifest is null || manifest.Fingerprint != fingerprint)
        {
            return null;
        }

        var nodes = new List<MemoryNode>(manifest.Nodes.Count);
        foreach (var (relativePath, entry) in manifest.Nodes)
        {
            var cacheFilePath = Path.Combine(cacheRoot, "nodes", entry.CacheFile);
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

            var fullPath = Path.Combine(storageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            nodes.Add(new MemoryNode
            {
                RelativePath = relativePath,
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
}
