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

        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var fingerprint = NodeService.ComputeNodeFingerprintForCache(fileSystemService, storageRoot, config.Handoffs.Directory);
        var nodes = await nodeService.GetAllNodesAsync(repositoryRoot, cancellationToken, bypassCache: true);
        if (fingerprint != NodeService.ComputeNodeFingerprintForCache(fileSystemService, storageRoot, config.Handoffs.Directory))
        {
            throw new InvalidOperationException("Memory files changed during index refresh. Retry the refresh.");
        }
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

        var indexRoot = RepositoryPathGuard.ResolveContainedPath(storageRoot, "indexes");
        await fileSystemService.WriteAllTextAsync(RepositoryPathGuard.ResolveContainedPath(indexRoot, "aliases.yaml"), YamlSerializer.Serialize(aliasOutput), cancellationToken);
        await fileSystemService.WriteAllTextAsync(RepositoryPathGuard.ResolveContainedPath(indexRoot, "tags.yaml"), YamlSerializer.Serialize(tagOutput), cancellationToken);
        await fileSystemService.WriteAllTextAsync(RepositoryPathGuard.ResolveContainedPath(indexRoot, "paths.yaml"), YamlSerializer.Serialize(paths), cancellationToken);

        await RefreshCacheAsync(indexRoot, nodes, fingerprint, cancellationToken);
    }

    private async Task RefreshCacheAsync(string indexRoot, IReadOnlyList<MemoryNode> nodes, string fingerprint, CancellationToken cancellationToken)
    {
        var cacheRoot = RepositoryPathGuard.ResolveContainedPath(indexRoot, "cache");
        var nodeCacheRoot = RepositoryPathGuard.ResolveContainedPath(cacheRoot, "nodes");
        fileSystemService.CreateDirectory(cacheRoot);
        fileSystemService.CreateDirectory(nodeCacheRoot);

        var manifestPath = RepositoryPathGuard.ResolveContainedPath(cacheRoot, "manifest.json");
        var manifest = await LoadManifestAsync(manifestPath, cancellationToken);
        var nextEntries = new Dictionary<string, IndexCacheManifestEntry>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var hashes = node.SourceHashes;
            var safeName = SanitizePath(node.RelativePath);
            var cacheFileName = $"{safeName}.json";
            var cacheFilePath = RepositoryPathGuard.ResolveContainedPath(nodeCacheRoot, cacheFileName);
            var cacheDocument = BuildCacheDocument(node, hashes);
            var json = JsonSerializer.Serialize(cacheDocument, JsonOptions) + Environment.NewLine;
            var cacheHash = Hash(json);

            if (manifest.Nodes.TryGetValue(node.RelativePath, out var existing) &&
                existing is not null && existing.CacheFile == cacheFileName &&
                existing.CacheHash == cacheHash &&
                fileSystemService.FileExists(cacheFilePath) &&
                Hash(await fileSystemService.ReadAllTextAsync(cacheFilePath, cancellationToken)) == cacheHash)
            {
                nextEntries[node.RelativePath] = existing;
                continue;
            }

            await fileSystemService.WriteAllTextAsync(cacheFilePath, json, cancellationToken);

            nextEntries[node.RelativePath] = new IndexCacheManifestEntry
            {
                CacheFile = cacheFileName,
                CacheHash = cacheHash,
                CachedAt = clock.UtcNow,
                FileHashes = hashes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            };
        }

        foreach (var stale in manifest.Nodes.Where(entry => !nextEntries.ContainsKey(entry.Key)))
        {
            if (stale.Value is not null && NodeListingCacheReader.IsSafeCacheFileName(stale.Value.CacheFile) &&
                !nextEntries.Values.Any(entry => entry.CacheFile == stale.Value.CacheFile))
            {
                fileSystemService.DeleteFile(RepositoryPathGuard.ResolveContainedPath(nodeCacheRoot, stale.Value.CacheFile));
            }
        }

        var nextManifest = new IndexCacheManifest
        {
            FormatVersion = IndexCacheManifest.CurrentFormatVersion,
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

        try
        {
            var json = await fileSystemService.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<IndexCacheManifest>(json, JsonOptions);
            return manifest is { Nodes: not null } ? manifest : new IndexCacheManifest();
        }
        catch (JsonException)
        {
            return new IndexCacheManifest();
        }
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
            SourceStartLines = node.SourceStartLines.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
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

    internal static string Hash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string SanitizePath(string relativePath) =>
        System.Text.RegularExpressions.Regex.IsMatch(relativePath, @"^[a-z0-9/-]+$")
            ? relativePath.Replace('/', '_')
            : "_legacy_" + Hash(relativePath);
}
