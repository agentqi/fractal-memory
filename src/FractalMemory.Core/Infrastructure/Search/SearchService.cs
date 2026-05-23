using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Infrastructure.Search;

public sealed class SearchService(
    IRepositoryService repositoryService,
    INodeService nodeService,
    IFileSystemService fileSystemService,
    IClock clock) : ISearchService
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string workingDirectory, string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var nodes = await nodeService.GetAllNodesAsync(repositoryRoot, cancellationToken);
        var profile = RetrievalPipeline.BuildQueryProfile(query);

        var results = new List<SearchResult>();
        foreach (var node in nodes)
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
                    ScoreBreakdown = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["exact_path"] = 1000,
                    },
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
                    ScoreBreakdown = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["exact_title"] = 900,
                    },
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
                    ScoreBreakdown = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["alias"] = 820,
                    },
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
                    ScoreBreakdown = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["tag"] = 760,
                    },
                });
            }

            candidates.AddRange(ToResults(node, title, RetrievalPipeline.BuildSearchCandidates(
                profile,
                node,
                title,
                "index.md",
                node.IndexContent,
                config.Retrieval.MaxSnippetLines)));
            candidates.AddRange(ToResults(node, title, RetrievalPipeline.BuildSearchCandidates(
                profile,
                node,
                title,
                "state.md",
                node.StateContent,
                config.Retrieval.MaxSnippetLines)));
            candidates.AddRange(ToResults(node, title, RetrievalPipeline.BuildSearchCandidates(
                profile,
                node,
                title,
                "timeline.md",
                node.TimelineContent,
                config.Retrieval.MaxSnippetLines)));
            candidates.AddRange(ToResults(node, title, RetrievalPipeline.BuildSearchCandidates(
                profile,
                node,
                title,
                "decisions.md",
                node.DecisionsContent,
                config.Retrieval.MaxSnippetLines)));

            var artifactsDirectory = Path.Combine(node.FullPath, "artifacts");
            foreach (var artifact in fileSystemService.EnumerateFiles(artifactsDirectory, "*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(artifact).Equals(".md", StringComparison.OrdinalIgnoreCase))
                {
                    var content = await fileSystemService.ReadAllTextAsync(artifact, cancellationToken);
                    var relativeArtifact = Path.GetRelativePath(artifactsDirectory, artifact).Replace(Path.DirectorySeparatorChar, '/');
                    candidates.AddRange(ToResults(node, title, RetrievalPipeline.BuildSearchCandidates(
                        profile,
                        node,
                        title,
                        $"artifacts/{relativeArtifact}",
                        content,
                        config.Retrieval.MaxSnippetLines)));
                }
            }

            var best = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.SourcePath, StringComparer.Ordinal)
                .FirstOrDefault();
            if (best is not null)
            {
                results.Add(best);
            }
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.RelativePath, StringComparer.Ordinal)
            .Take(config.Retrieval.DiagnosticTopK)
            .ToArray();
    }

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

        var items = nodes
            .Where(node => string.IsNullOrWhiteSpace(scope) || node.RelativePath.StartsWith(scope!, StringComparison.OrdinalIgnoreCase))
            .Select(node =>
            {
                var files = new[] { "index.md", "state.md", "timeline.md", "decisions.md" }
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

    internal static IReadOnlyList<SearchResult> BuildNodeEvidence(MemoryNode node, int maxSnippetLines) =>
        RetrievalPipeline.BuildNodeEvidenceResults(node, maxSnippetLines);

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
}
