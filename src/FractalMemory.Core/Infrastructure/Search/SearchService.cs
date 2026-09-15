using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Domain.Rules;
using FractalMemory.Core.Infrastructure.Files;
using FractalMemory.Core.Infrastructure.Parsing;
using YamlDotNet.Serialization;

namespace FractalMemory.Core.Infrastructure.Search;

public sealed class SearchService(
    IRepositoryService repositoryService,
    INodeService nodeService,
    IFileSystemService fileSystemService,
    IClock clock) : ISearchService
{
    private static readonly IDeserializer PathIndexDeserializer = new DeserializerBuilder().Build();
    private static readonly string[] NodeContractFileNames = ["index", "state", "timeline", "decisions"];

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string workingDirectory, string query, CancellationToken cancellationToken, int? limit = null, string? scope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Search limit must be a positive integer.");
        }

        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var normalizedScope = NormalizeScope(scope);
        var nodes = await nodeService.GetAllNodesAsync(repositoryRoot, cancellationToken, scope: normalizedScope);
        var profile = RetrievalPipeline.BuildQueryProfile(query);

        var perNodeBests = new SearchResult?[nodes.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, nodes.Count),
            new ParallelOptions { CancellationToken = cancellationToken },
            async (index, token) =>
            {
                perNodeBests[index] = await ScoreNodeAsync(
                    nodes[index],
                    profile,
                    query,
                    config.Retrieval.MaxSnippetLines,
                    token);
            });

        var results = perNodeBests.Where(item => item is not null).Cast<SearchResult>().ToList();

        var effectiveLimit = limit is > 0 ? limit.Value : config.Retrieval.DiagnosticTopK;
        return results
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => FileTypePriority(result.MatchedFile))
            .ThenBy(result => result.RelativePath, StringComparer.Ordinal)
            .Take(effectiveLimit)
            .ToArray();
    }

    internal static int FileTypePriority(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName) switch
        {
            "state" => 4,
            "decisions" => 3,
            "index" => 2,
            "timeline" => 1,
            _ => 0,
        };

    public async Task<IReadOnlyList<RecentItem>> GetRecentAsync(
        string workingDirectory,
        int limit,
        int days,
        string? scope,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Recent item limit must be a positive integer.");
        }

        if (days < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Recent day range cannot be negative.");
        }

        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var cutoff = clock.UtcNow.AddDays(-days);
        var normalizedScope = NormalizeScope(scope);
        var pathTitles = await LoadPathTitlesAsync(storageRoot, cancellationToken);
        var latestByNode = new Dictionary<string, RecentNodeFile>(StringComparer.Ordinal);

        foreach (var file in EnumerateRecentNodeFiles(storageRoot, config.Handoffs.Directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(file);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(storageRoot, directory).Replace(Path.DirectorySeparatorChar, '/');
            if (normalizedScope is not null && !MatchesScope(relativePath, normalizedScope))
            {
                continue;
            }

            var lastModified = fileSystemService.GetLastWriteTimeUtc(file);
            var candidate = new RecentNodeFile(relativePath, Path.GetFileName(file), lastModified);
            if (!latestByNode.TryGetValue(relativePath, out var current) || candidate.LastModified > current.LastModified)
            {
                latestByNode[relativePath] = candidate;
            }
        }

        var items = latestByNode.Values
            .Where(item => item.LastModified >= cutoff)
            .OrderByDescending(item => item.LastModified)
            .Take(limit)
            .Select(item => new RecentItem
            {
                RelativePath = item.RelativePath,
                Title = pathTitles.TryGetValue(item.RelativePath, out var title) && !string.IsNullOrWhiteSpace(title)
                    ? title
                    : Path.GetFileName(item.RelativePath),
                FileName = item.FileName,
                LastModified = item.LastModified,
            })
            .ToArray();

        return items;
    }

    private sealed record RecentNodeFile(string RelativePath, string FileName, DateTimeOffset LastModified);

    private async Task<SearchResult?> ScoreNodeAsync(
        MemoryNode node,
        RetrievalQueryProfile profile,
        string query,
        int maxSnippetLines,
        CancellationToken cancellationToken)
    {
        var title = node.Metadata.Title ?? Path.GetFileName(node.RelativePath);
        var candidates = new List<SearchResult>();

        if (node.RelativePath.Equals(profile.LoweredQuery, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(new SearchResult
            {
                RelativePath = node.RelativePath,
                Title = title,
                MatchedFile = "path",
                SourcePath = node.RelativePath,
                Snippet = node.RelativePath,
                Score = 1000,
                ScoreBreakdown = new Dictionary<string, int>(StringComparer.Ordinal) { ["exact_path"] = 1000 },
            });
        }

        if (title.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(new SearchResult
            {
                RelativePath = node.RelativePath,
                Title = title,
                MatchedFile = node.IndexFileName,
                SourcePath = $"{node.RelativePath}/{node.IndexFileName}",
                Snippet = title,
                Score = 900,
                ScoreBreakdown = new Dictionary<string, int>(StringComparer.Ordinal) { ["exact_title"] = 900 },
            });
        }

        if (node.Metadata.Aliases.Any(alias => alias.Equals(query, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(new SearchResult
            {
                RelativePath = node.RelativePath,
                Title = title,
                MatchedFile = node.IndexFileName,
                SourcePath = $"{node.RelativePath}/{node.IndexFileName}",
                Snippet = $"Alias match: {query}",
                Score = 820,
                ScoreBreakdown = new Dictionary<string, int>(StringComparer.Ordinal) { ["alias"] = 820 },
            });
        }

        if (node.Metadata.Tags.Any(tag => tag.Equals(query, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(new SearchResult
            {
                RelativePath = node.RelativePath,
                Title = title,
                MatchedFile = node.IndexFileName,
                SourcePath = $"{node.RelativePath}/{node.IndexFileName}",
                Snippet = $"Tag match: {query}",
                Score = 760,
                ScoreBreakdown = new Dictionary<string, int>(StringComparer.Ordinal) { ["tag"] = 760 },
            });
        }

        foreach (var (fileName, content) in EnumerateNodeFiles(node))
        {
            candidates.AddRange(ToResults(node, title, RetrievalPipeline.BuildSearchCandidates(
                profile, node, title, fileName, content, maxSnippetLines)));
        }

        var artifactsDirectory = Path.Combine(node.FullPath, "artifacts");
        foreach (var artifact in fileSystemService.EnumerateFiles(artifactsDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RepositoryPathGuard.EnsureContainedPath(node.FullPath, artifact);
            if (!IsSearchableArtifact(artifact))
            {
                continue;
            }

            var content = await ReadArtifactContentAsync(fileSystemService, artifact, cancellationToken);
            var relativeArtifact = Path.GetRelativePath(artifactsDirectory, artifact).Replace(Path.DirectorySeparatorChar, '/');
            candidates.AddRange(ToResults(node, title, RetrievalPipeline.BuildSearchCandidates(
                profile, node, title, $"artifacts/{relativeArtifact}", content, maxSnippetLines)));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => FileTypePriority(candidate.MatchedFile))
            .ThenBy(candidate => candidate.SourcePath, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IEnumerable<(string FileName, string Content)> EnumerateNodeFiles(MemoryNode node)
    {
        yield return (node.IndexFileName, node.IndexContent);
        yield return (node.StateFileName, node.StateContent);
        yield return (node.TimelineFileName, node.TimelineContent);
        yield return (node.DecisionsFileName, node.DecisionsContent);
    }

    internal static IReadOnlyList<SearchResult> BuildNodeEvidence(MemoryNode node, int maxSnippetLines) =>
        RetrievalPipeline.BuildNodeEvidenceResults(node, maxSnippetLines);

    private static string? NormalizeScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        var normalized = scope.Trim().Replace('\\', '/').Trim('/');
        return normalized.Length == 0 ? null : NodePathRules.Normalize(normalized.ToLowerInvariant());
    }

    private static bool MatchesScope(string relativePath, string scope)
    {
        if (relativePath.Equals(scope, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = scope + "/";
        return relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SearchResult> ToResults(
        MemoryNode node,
        string title,
        IReadOnlyList<SnippetCandidate> candidates) =>
        candidates.Select(candidate => new SearchResult
        {
            RelativePath = node.RelativePath,
            Title = title,
            MatchedFile = candidate.MatchedFile,
            SourcePath = candidate.SourcePath,
            Snippet = candidate.Snippet.Length > 240 ? candidate.Snippet[..240] + "..." : candidate.Snippet,
            SectionHeading = candidate.SectionHeading,
            StartLine = RetrievalPipeline.ToSourceLine(node, candidate.MatchedFile, candidate.StartLine),
            EndLine = RetrievalPipeline.ToSourceLine(node, candidate.MatchedFile, candidate.EndLine),
            Score = candidate.Score,
            ScoreBreakdown = candidate.Breakdown,
        }).ToArray();

    private static bool IsSearchableArtifact(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadPathTitlesAsync(string storageRoot, CancellationToken cancellationToken)
    {
        var pathsIndex = RepositoryPathGuard.ResolveContainedPath(storageRoot, "indexes/paths.yaml");
        if (!fileSystemService.FileExists(pathsIndex))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var yaml = await fileSystemService.ReadAllTextAsync(pathsIndex, cancellationToken);
            var parsed = PathIndexDeserializer.Deserialize<Dictionary<string, string>>(yaml);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private IEnumerable<string> EnumerateRecentNodeFiles(string storageRoot, string handoffDirectory)
    {
        foreach (var path in fileSystemService.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories))
        {
            RepositoryPathGuard.EnsureContainedPath(storageRoot, path);
            if (!IsSearchableArtifact(path) || IsExcludedMemoryPath(storageRoot, path, handoffDirectory))
            {
                continue;
            }

            if (NodeContractFileNames.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private static bool IsExcludedMemoryPath(string storageRoot, string path, string handoffDirectory)
    {
        var relative = Path.GetRelativePath(storageRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return relative.StartsWith("templates/", StringComparison.Ordinal) ||
            relative.StartsWith("archive/", StringComparison.Ordinal) ||
            relative.StartsWith("handoffs/", StringComparison.Ordinal) ||
            relative.StartsWith(handoffDirectory + "/", StringComparison.Ordinal) ||
            relative.StartsWith("indexes/", StringComparison.Ordinal) ||
            relative.Contains("/artifacts/", StringComparison.Ordinal);
    }

    private static async Task<string> ReadArtifactContentAsync(
        IFileSystemService fileSystemService,
        string path,
        CancellationToken cancellationToken)
    {
        var content = await fileSystemService.ReadAllTextAsync(path, cancellationToken);
        var extension = Path.GetExtension(path);
        return extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
            ? HtmlTextExtractor.ToText(content)
            : content;
    }
}
