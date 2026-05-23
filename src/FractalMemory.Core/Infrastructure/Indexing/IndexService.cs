using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Infrastructure.Search;
using YamlDotNet.Serialization;

namespace FractalMemory.Core.Infrastructure.Indexing;

public sealed class IndexService(
    IRepositoryService repositoryService,
    INodeService nodeService,
    IFileSystemService fileSystemService,
    IStructuredMemoryService structuredMemoryService,
    IClock clock) : IIndexService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public async Task RefreshAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");

        var nodes = await nodeService.GetAllNodesAsync(repositoryRoot, cancellationToken);
        var aliases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var tags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            paths[node.RelativePath] = node.Metadata.Title ?? Path.GetFileName(node.RelativePath);

            foreach (var alias in node.Metadata.Aliases)
            {
                aliases[alias] = aliases.TryGetValue(alias, out var existing)
                    ? existing.Concat([node.RelativePath]).Distinct(StringComparer.Ordinal).ToArray()
                    : [node.RelativePath];
            }

            foreach (var tag in node.Metadata.Tags)
            {
                tags[tag] = tags.TryGetValue(tag, out var existing)
                    ? existing.Concat([node.RelativePath]).Distinct(StringComparer.Ordinal).ToArray()
                    : [node.RelativePath];
            }
        }

        var serializer = new SerializerBuilder().Build();
        var indexRoot = Path.Combine(repositoryService.GetStorageRoot(repositoryRoot), "indexes");
        await fileSystemService.WriteAllTextAsync(Path.Combine(indexRoot, "aliases.yaml"), serializer.Serialize(aliases), cancellationToken);
        await fileSystemService.WriteAllTextAsync(Path.Combine(indexRoot, "tags.yaml"), serializer.Serialize(tags), cancellationToken);
        await fileSystemService.WriteAllTextAsync(Path.Combine(indexRoot, "paths.yaml"), serializer.Serialize(paths), cancellationToken);

        await RefreshCacheAsync(indexRoot, nodes, cancellationToken);
    }

    private async Task RefreshCacheAsync(string indexRoot, IReadOnlyList<MemoryNode> nodes, CancellationToken cancellationToken)
    {
        var cacheRoot = Path.Combine(indexRoot, "cache");
        var nodeCacheRoot = Path.Combine(cacheRoot, "nodes");
        fileSystemService.CreateDirectory(cacheRoot);
        fileSystemService.CreateDirectory(nodeCacheRoot);

        var manifestPath = Path.Combine(cacheRoot, "manifest.json");
        var manifest = await LoadManifestAsync(manifestPath, cancellationToken);
        var nextEntries = new Dictionary<string, IndexCacheManifestEntry>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var hashes = BuildFileHashes(node);
            var safeName = SanitizePath(node.RelativePath);
            var cacheFileName = $"{safeName}.json";
            var cacheFilePath = Path.Combine(nodeCacheRoot, cacheFileName);

            if (manifest.Nodes.TryGetValue(node.RelativePath, out var existing) &&
                existing.FileHashes.Count == hashes.Count &&
                existing.FileHashes.OrderBy(item => item.Key).SequenceEqual(hashes.OrderBy(item => item.Key)))
            {
                nextEntries[node.RelativePath] = existing;
                continue;
            }

            var cacheDocument = BuildCacheDocument(node, hashes);
            var json = JsonSerializer.Serialize(cacheDocument, JsonOptions);
            await fileSystemService.WriteAllTextAsync(cacheFilePath, json + Environment.NewLine, cancellationToken);

            nextEntries[node.RelativePath] = new IndexCacheManifestEntry
            {
                CacheFile = cacheFileName,
                CachedAt = clock.UtcNow,
                FileHashes = hashes,
            };
        }

        foreach (var stale in manifest.Nodes.Where(entry => !nextEntries.ContainsKey(entry.Key)))
        {
            fileSystemService.DeleteFile(Path.Combine(nodeCacheRoot, stale.Value.CacheFile));
        }

        var nextManifest = new IndexCacheManifest
        {
            RefreshedAt = clock.UtcNow,
            Nodes = nextEntries,
        };
        var manifestJson = JsonSerializer.Serialize(nextManifest, JsonOptions);
        await fileSystemService.WriteAllTextAsync(manifestPath, manifestJson + Environment.NewLine, cancellationToken);
    }

    private async Task<IndexCacheManifest> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        if (!fileSystemService.FileExists(manifestPath))
        {
            return new IndexCacheManifest();
        }

        var json = await fileSystemService.ReadAllTextAsync(manifestPath, cancellationToken);
        return JsonSerializer.Deserialize<IndexCacheManifest>(json, JsonOptions) ?? new IndexCacheManifest();
    }

    private NodeCacheDocument BuildCacheDocument(MemoryNode node, IReadOnlyDictionary<string, string> hashes)
    {
        var evidence = RetrievalPipeline.BuildNodeEvidenceResults(node, 4);
        return new NodeCacheDocument
        {
            RelativePath = node.RelativePath,
            Metadata = node.Metadata,
            StateSchema = structuredMemoryService.Parse(node.StateContent, node.RelativePath),
            FileHashes = hashes,
            Files = new Dictionary<string, CachedMarkdownFile>(StringComparer.Ordinal)
            {
                [node.IndexFileName] = BuildCachedFile(node.IndexContent),
                [node.StateFileName] = BuildCachedFile(node.StateContent),
                [node.TimelineFileName] = BuildCachedFile(node.TimelineContent),
                [node.DecisionsFileName] = BuildCachedFile(node.DecisionsContent),
            },
            Evidence = evidence.Select(result => new CachedEvidence
            {
                SourcePath = result.SourcePath,
                MatchedFile = result.MatchedFile,
                SectionHeading = result.SectionHeading,
                Snippet = result.Snippet,
                StartLine = result.StartLine,
                EndLine = result.EndLine,
                Score = result.Score,
            }).ToArray(),
        };
    }

    private static CachedMarkdownFile BuildCachedFile(string content)
    {
        var sections = RetrievalPipeline.ParseSections(content);
        return new CachedMarkdownFile
        {
            Sections = sections.Select(section => new CachedSection
            {
                Heading = section.Heading,
                StartLine = section.StartLine,
                EndLine = section.EndLine,
                Lines = section.Lines,
            }).ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, string> BuildFileHashes(MemoryNode node) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [node.IndexFileName] = Hash(node.IndexContent),
            [node.StateFileName] = Hash(node.StateContent),
            [node.TimelineFileName] = Hash(node.TimelineContent),
            [node.DecisionsFileName] = Hash(node.DecisionsContent),
        };

    private static string Hash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string SanitizePath(string relativePath) =>
        relativePath.Replace('/', '_').Replace('\\', '_');

    private sealed class IndexCacheManifest
    {
        public DateTimeOffset RefreshedAt { get; init; }
        public Dictionary<string, IndexCacheManifestEntry> Nodes { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class IndexCacheManifestEntry
    {
        public required string CacheFile { get; init; }
        public required IReadOnlyDictionary<string, string> FileHashes { get; init; }
        public DateTimeOffset CachedAt { get; init; }
    }

    private sealed class NodeCacheDocument
    {
        public required string RelativePath { get; init; }
        public required NodeMetadata Metadata { get; init; }
        public required StructuredMemoryFields StateSchema { get; init; }
        public required IReadOnlyDictionary<string, string> FileHashes { get; init; }
        public required IReadOnlyDictionary<string, CachedMarkdownFile> Files { get; init; }
        public required IReadOnlyList<CachedEvidence> Evidence { get; init; }
    }

    private sealed class CachedMarkdownFile
    {
        public IReadOnlyList<CachedSection> Sections { get; init; } = [];
    }

    private sealed class CachedSection
    {
        public required string Heading { get; init; }
        public int StartLine { get; init; }
        public int EndLine { get; init; }
        public IReadOnlyList<string> Lines { get; init; } = [];
    }

    private sealed class CachedEvidence
    {
        public required string SourcePath { get; init; }
        public required string MatchedFile { get; init; }
        public string? SectionHeading { get; init; }
        public required string Snippet { get; init; }
        public int? StartLine { get; init; }
        public int? EndLine { get; init; }
        public int Score { get; init; }
    }
}
