using System.Text.Json;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

public sealed class RetrievalAndSchemaTests
{
    [Fact]
    public async Task SearchReranksExactDateAndEventAboveThematicNearMiss()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "research/caroline-support", CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "research/caroline-activist", CancellationToken.None);

        var supportRoot = Path.Combine(temp, ".fractal-memory", "research", "caroline-support");
        await File.WriteAllTextAsync(Path.Combine(supportRoot, "state.md"), """
            ---
            title: Caroline Support Group
            summary: Caroline attended the support group.
            ---

            ## Current Objective

            Recover the date of Caroline's LGBTQ support group visit.

            ## Supporting Notes

            - Caroline attended an LGBTQ support group on 7 May 2023.
            """);
        await File.WriteAllTextAsync(Path.Combine(supportRoot, "timeline.md"), """
            ---
            title: Timeline
            ---

            - 8 May 2023: Caroline said she went to the LGBTQ support group yesterday and found it powerful.
            """);
        await File.WriteAllTextAsync(Path.Combine(supportRoot, "decisions.md"), """
            ---
            title: Decisions
            ---

            - Caroline attended an LGBTQ support group on 7 May 2023.
            """);

        var activistRoot = Path.Combine(temp, ".fractal-memory", "research", "caroline-activist");
        await File.WriteAllTextAsync(Path.Combine(activistRoot, "state.md"), """
            ---
            title: Caroline Activist Group
            summary: Caroline joined an activist group.
            ---

            - Caroline joined a new LGBTQ activist group on 11 July 2023.
            - Caroline is passionate about LGBTQ rights and community support.
            """);

        var results = await searchService.SearchAsync(temp, "When did Caroline go to the LGBTQ support group?", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("research/caroline-support", results[0].RelativePath);
        Assert.Equal("research/caroline-support/state.md", results[0].SourcePath);
        Assert.True(results[0].ScoreBreakdown.ContainsKey("exact_date_match") || results[0].ScoreBreakdown.ContainsKey("normalized_date_match"));
        Assert.DoesNotContain("broad_thematic_penalty", results[0].ScoreBreakdown.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task SearchKeepsBranchScopedNodeAheadOfSimilarNeighbor()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/govos", CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/flowone", CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "projects", "govos", "state.md"), """
            ---
            title: GovOS
            ---

            ## Current Objective

            Finalize the GovOS authentication direction with organization-scoped tokens.
            """);
        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "projects", "flowone", "state.md"), """
            ---
            title: FlowOne
            ---

            ## Current Objective

            Review authentication direction for FlowOne traceability tooling.
            """);

        var results = await searchService.SearchAsync(temp, "Show only GovOS authentication direction", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("projects/govos", results[0].RelativePath);
    }

    [Fact]
    public async Task SearchReturnsLocalSnippetInsteadOfWholeFileDump()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/snippet", CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "projects", "snippet", "state.md"), """
            ---
            title: Snippet Demo
            ---

            ## Overview

            This section is generic setup text and should not be returned for the targeted query.

            ## Active Constraints

            - Keep the benchmark packet under 220 tokens.
            - Prefer direct restatement over broad planning.

            ## Open Questions

            - What evidence should be prioritized first?
            """);

        var results = await searchService.SearchAsync(temp, "220 tokens", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("Active Constraints", results[0].SectionHeading);
        Assert.Contains("220 tokens", results[0].Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("generic setup text", results[0].Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(results[0].StartLine);
        Assert.NotNull(results[0].EndLine);
    }

    [Fact]
    public async Task ExportBuildsSmallerAnswerContextThanDiagnostics()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var exportService = provider.GetRequiredService<IExportService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/answer-packet", CancellationToken.None);
        var nodeRoot = Path.Combine(temp, ".fractal-memory", "projects", "answer-packet");

        await File.WriteAllTextAsync(Path.Combine(nodeRoot, "state.md"), """
            ---
            title: Answer Packet State
            last_updated: 2026-04-03T12:00:00Z
            ---

            ## Project / Branch

            projects/answer-packet

            ## Current Objective

            Ship the top-priority retrieval sprint.

            ## Active Constraints

            - Keep answer packets compact.
            - Preserve deterministic local behavior.

            ## Next Best Actions

            - Add reranking tests.
            - Add cache invalidation tests.

            ## Open Questions

            - Which snippet fields should be mandatory?
            """);
        await File.WriteAllTextAsync(Path.Combine(nodeRoot, "decisions.md"), """
            ---
            title: Decisions
            ---

            ## Key Decisions in Force

            - Prefer snippet-level evidence over whole-file dumps.
            - Keep answer top-k smaller than diagnostics.
            """);
        await File.WriteAllTextAsync(Path.Combine(nodeRoot, "timeline.md"), """
            ---
            title: Timeline
            ---

            - 2026-04-01: Benchmarks showed correct hits but weak top-1 ranking.
            - 2026-04-02: Benchmarks showed answer packets were too large.
            - 2026-04-03: Sprint prioritized reranking, snippets, caching, and schema support.
            """);

        var export = await exportService.ExportAsync(temp, "projects/answer-packet", ExportMode.Standard, CancellationToken.None);

        Assert.NotNull(export.AnswerContext);
        Assert.Equal("projects/answer-packet", export.AnswerContext!.ProjectBranch);
        Assert.True(export.AnswerContext.SupportingSources.Count <= 3);
        Assert.True(export.AnswerContext.DiagnosticResults.Count >= export.AnswerContext.SupportingSources.Count);
        Assert.Contains(export.AnswerContext.KeyPriorDecisions, item => item.Contains("snippet-level evidence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IndexRefreshCachesAndSkipsUnchangedFiles()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var indexService = provider.GetRequiredService<IIndexService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/cache-alpha", CancellationToken.None);

        await indexService.RefreshAsync(temp, CancellationToken.None);
        var cacheFile = Path.Combine(temp, ".fractal-memory", "indexes", "cache", "nodes", "projects_cache-alpha.json");
        Assert.True(File.Exists(cacheFile));
        var firstWrite = File.GetLastWriteTimeUtc(cacheFile);

        await Task.Delay(1200);
        await indexService.RefreshAsync(temp, CancellationToken.None);
        var secondWrite = File.GetLastWriteTimeUtc(cacheFile);

        Assert.Equal(firstWrite, secondWrite);
    }

    [Fact]
    public async Task IndexRefreshInvalidatesChangedFiles()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var indexService = provider.GetRequiredService<IIndexService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/cache-beta", CancellationToken.None);

        await indexService.RefreshAsync(temp, CancellationToken.None);
        var cacheFile = Path.Combine(temp, ".fractal-memory", "indexes", "cache", "nodes", "projects_cache-beta.json");
        var manifestPath = Path.Combine(temp, ".fractal-memory", "indexes", "cache", "manifest.json");
        var firstWrite = File.GetLastWriteTimeUtc(cacheFile);
        var firstManifest = await File.ReadAllTextAsync(manifestPath);

        await Task.Delay(1200);
        await File.AppendAllTextAsync(Path.Combine(temp, ".fractal-memory", "projects", "cache-beta", "state.md"), Environment.NewLine + "- Cache invalidation marker.");
        await indexService.RefreshAsync(temp, CancellationToken.None);

        var secondWrite = File.GetLastWriteTimeUtc(cacheFile);
        var secondManifest = await File.ReadAllTextAsync(manifestPath);

        Assert.True(secondWrite > firstWrite);
        Assert.NotEqual(firstManifest, secondManifest);
    }

    [Fact]
    public void StructuredMemoryParserHandlesSchemaAndLegacyMarkdown()
    {
        using var provider = TestEnvironment.CreateServices();
        var structuredMemory = provider.GetRequiredService<IStructuredMemoryService>();

        var schema = structuredMemory.Parse("""
            ## Project / Branch

            projects/fractal-memory-cli

            ## Current Objective

            Finish the retrieval sprint.

            ## Active Constraints

            - Keep context small.

            ## Next Best Actions

            - Add reranking tests.
            """, "projects/fallback");

        var legacy = structuredMemory.Parse("""
            Current objective is finishing the retrieval sprint.
            Constraint: keep everything deterministic.
            What should we cache first?
            """, "projects/fallback");

        Assert.Equal("projects/fractal-memory-cli", schema.ProjectBranch);
        Assert.Equal("Finish the retrieval sprint.", schema.CurrentObjective);
        Assert.Contains("Keep context small.", schema.ActiveConstraints);
        Assert.Contains("Add reranking tests.", schema.NextBestActions);

        Assert.Equal("projects/fallback", legacy.ProjectBranch);
        Assert.Contains("finishing the retrieval sprint", legacy.CurrentObjective, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(legacy.ActiveConstraints, item => item.Contains("deterministic", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(legacy.OpenQuestions, item => item.Contains("cache first", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CacheManifestPreservesInspectableNodeEntries()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var indexService = provider.GetRequiredService<IIndexService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/inspectable-cache", CancellationToken.None);
        await indexService.RefreshAsync(temp, CancellationToken.None);

        var manifestPath = Path.Combine(temp, ".fractal-memory", "indexes", "cache", "manifest.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("Nodes", out var nodes));
        Assert.True(nodes.TryGetProperty("projects/inspectable-cache", out var nodeEntry));
        Assert.True(nodeEntry.TryGetProperty("CacheFile", out _));
        Assert.True(nodeEntry.TryGetProperty("FileHashes", out _));
    }
}
