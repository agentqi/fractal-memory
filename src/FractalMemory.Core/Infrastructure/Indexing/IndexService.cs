using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Infrastructure.Files;
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
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly ISerializer YamlSerializer = new SerializerBuilder().Build();

    public async Task RefreshAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");

        var nodes = await nodeService.GetAllNodesAsync(repositoryRoot, cancellationToken);
        var aliases = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var tags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            paths[node.RelativePath] = node.Metadata.Title ?? Path.GetFileName(node.RelativePath);

            foreach (var alias in node.Metadata.Aliases)
            {
                if (!aliases.TryGetValue(alias, out var bucket))
                {
                    bucket = new HashSet<string>(StringComparer.Ordinal);
                    aliases[alias] = bucket;
                }

                bucket.Add(node.RelativePath);
            }

            foreach (var tag in node.Metadata.Tags)
            {
                if (!tags.TryGetValue(tag, out var bucket))
                {
                    bucket = new HashSet<string>(StringComparer.Ordinal);
                    tags[tag] = bucket;
                }

                bucket.Add(node.RelativePath);
            }
        }

        var aliasOutput = aliases.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.OrderBy(item => item, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var tagOutput = tags.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.OrderBy(item => item, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        var indexRoot = Path.Combine(repositoryService.GetStorageRoot(repositoryRoot), "indexes");
        await fileSystemService.WriteAllTextAsync(Path.Combine(indexRoot, "aliases.yaml"), YamlSerializer.Serialize(aliasOutput), cancellationToken);
        await fileSystemService.WriteAllTextAsync(Path.Combine(indexRoot, "tags.yaml"), YamlSerializer.Serialize(tagOutput), cancellationToken);
        await fileSystemService.WriteAllTextAsync(Path.Combine(indexRoot, "paths.yaml"), YamlSerializer.Serialize(paths), cancellationToken);

        var fingerprint = NodeService.ComputeNodeFingerprintForCache(fileSystemService, repositoryService.GetStorageRoot(repositoryRoot));
        await RefreshCacheAsync(indexRoot, nodes, fingerprint, cancellationToken);
    }

    private async Task RefreshCacheAsync(string indexRoot, IReadOnlyList<MemoryNode> nodes, DateTimeOffset fingerprint, CancellationToken cancellationToken)
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
                HashesEqual(existing.FileHashes, hashes) &&
                fileSystemService.FileExists(Path.Combine(nodeCacheRoot, existing.CacheFile)))
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
                FileHashes = hashes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            };
        }

        foreach (var stale in manifest.Nodes.Where(entry => !nextEntries.ContainsKey(entry.Key)))
        {
            fileSystemService.DeleteFile(Path.Combine(nodeCacheRoot, stale.Value.CacheFile));
        }

        var nextManifest = new IndexCacheManifest
        {
            RefreshedAt = clock.UtcNow,
            Fingerprint = fingerprint,
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
            FileHashes = hashes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            IndexFileName = node.IndexFileName,
            StateFileName = node.StateFileName,
            TimelineFileName = node.TimelineFileName,
            DecisionsFileName = node.DecisionsFileName,
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
            }).ToList(),
        };
    }

    private static CachedMarkdownFile BuildCachedFile(string content)
    {
        var sections = RetrievalPipeline.ParseSections(content);
        return new CachedMarkdownFile
        {
            RawContent = content,
            Sections = sections.Select(section => new CachedSection
            {
                Heading = section.Heading,
                StartLine = section.StartLine,
                EndLine = section.EndLine,
                Lines = section.Lines.ToList(),
            }).ToList(),
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

    private static bool HashesEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Hash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string SanitizePath(string relativePath) =>
        relativePath.Replace('/', '_').Replace('\\', '_');
}
