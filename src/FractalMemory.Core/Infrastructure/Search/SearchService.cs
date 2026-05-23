using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Infrastructure.Parsing;

namespace FractalMemory.Core.Infrastructure.Search;

public sealed class SearchService(
    IRepositoryService repositoryService,
    INodeService nodeService,
    IFileSystemService fileSystemService,
    IClock clock) : ISearchService
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string workingDirectory, string query, CancellationToken cancellationToken, int? limit = null, string? scope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var allNodes = await nodeService.GetAllNodesAsync(repositoryRoot, cancellationToken);
        var normalizedScope = NormalizeScope(scope);
        var nodes = normalizedScope is null
            ? (IReadOnlyList<MemoryNode>)allNodes
            : allNodes.Where(node => MatchesScope(node.RelativePath, normalizedScope)).ToArray();
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
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var cutoff = clock.UtcNow.AddDays(-days);
        var nodes = await nodeService.GetAllNodesAsync(repositoryRoot, cancellationToken);

        var normalizedScope = NormalizeScope(scope);
        var items = nodes
            .Where(node => normalizedScope is null || MatchesScope(node.RelativePath, normalizedScope))
            .Select(node =>
            {
                var files = new[] { node.IndexFileName, node.StateFileName, node.TimelineFileName, node.DecisionsFileName }
                    .Select(file => Path.Combine(node.FullPath, file))
                    .Where(fileSystemService.FileExists)
                    .Select(path => new { Path = path, LastModified = fileSystemService.GetLastWriteTimeUtc(path) })
                    .OrderByDescending(item => item.LastModified)
                    .First();

                return new RecentItem
                {
                    RelativePath = node.RelativePath,
                    Title = node.Metadata.Title ?? Path.GetFileName(node.RelativePath),
                    FileName = Path.GetFileName(files.Path),
                    LastModified = files.LastModified,
                };
            })
            .Where(item => item.LastModified >= cutoff)
            .OrderByDescending(item => item.LastModified)
            .Take(limit)
            .ToArray();

        return items;
    }

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
                MatchedFile = "index.md",
                SourcePath = $"{node.RelativePath}/index.md",
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
                MatchedFile = "index.md",
                SourcePath = $"{node.RelativePath}/index.md",
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
                MatchedFile = "index.md",
                SourcePath = $"{node.RelativePath}/index.md",
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
        return normalized.Length == 0 ? null : normalized;
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
            StartLine = candidate.StartLine,
            EndLine = candidate.EndLine,
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
