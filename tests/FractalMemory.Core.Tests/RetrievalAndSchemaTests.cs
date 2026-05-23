using System.Text.Json;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
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
    public async Task SearchHonorsExplicitLimitAboveDiagnosticTopK()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        for (var i = 0; i < 15; i++)
        {
            await nodeService.CreateNodeAsync(temp, $"projects/limit-{i:00}", CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(temp, ".fractal-memory", "projects", $"limit-{i:00}", "state.md"),
                $"""
                ---
                title: Limit Node {i:00}
                ---

                ## Current Objective

                Investigate retrieval limit propagation node {i:00}.
                """);
        }

        var defaultResults = await searchService.SearchAsync(temp, "retrieval limit propagation", CancellationToken.None);
        var expandedResults = await searchService.SearchAsync(temp, "retrieval limit propagation", CancellationToken.None, limit: 15);

        Assert.True(defaultResults.Count <= 10);
        Assert.True(expandedResults.Count > defaultResults.Count);
    }

    [Fact]
    public async Task ThematicOverlapWithoutPhraseMatchIsPenalized()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/exact-phrase", CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/topic-only", CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "projects", "exact-phrase", "state.md"), """
            ---
            title: Exact Phrase
            ---

            ## Current Objective

            Document the deployment rollback procedure for the staging tier.
            """);
        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "projects", "topic-only", "state.md"), """
            ---
            title: Topic Only
            ---

            ## Current Objective

            Track tier deployment metrics and rollback failure rates separately.
            """);

        var results = await searchService.SearchAsync(temp, "deployment rollback procedure", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("projects/exact-phrase", results[0].RelativePath);
        Assert.DoesNotContain("broad_thematic_penalty", results[0].ScoreBreakdown.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task NodeListingCacheInvalidatesWhenStateFileChanges()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/cache-invalidation", CancellationToken.None);
        var statePath = Path.Combine(temp, ".fractal-memory", "projects", "cache-invalidation", "state.md");
        await File.WriteAllTextAsync(statePath, """
            ---
            title: Cache Invalidation
            ---

            Initial waypoint marker before the update happens.
            """);

        var beforeUpdate = await searchService.SearchAsync(temp, "initial waypoint marker", CancellationToken.None);
        Assert.NotEmpty(beforeUpdate);

        await Task.Delay(1100);
        await File.WriteAllTextAsync(statePath, """
            ---
            title: Cache Invalidation
            ---

            Subsequent breadcrumb signal after the cache should have flushed.
            """);

        var afterStaleQuery = await searchService.SearchAsync(temp, "initial waypoint marker", CancellationToken.None);
        var afterFreshQuery = await searchService.SearchAsync(temp, "subsequent breadcrumb signal", CancellationToken.None);

        Assert.Empty(afterStaleQuery);
        Assert.NotEmpty(afterFreshQuery);
    }

    [Fact]
    public async Task RecentScopeRequiresPathBoundary()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/alpha", CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects-archive/beta", CancellationToken.None);

        var scoped = await searchService.GetRecentAsync(temp, 50, 30, "projects", CancellationToken.None);
        var withSlash = await searchService.GetRecentAsync(temp, 50, 30, "projects/", CancellationToken.None);

        Assert.All(scoped, item => Assert.StartsWith("projects/", item.RelativePath, StringComparison.Ordinal));
        Assert.All(withSlash, item => Assert.StartsWith("projects/", item.RelativePath, StringComparison.Ordinal));
        Assert.DoesNotContain(scoped, item => item.RelativePath.StartsWith("projects-archive/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TemporalMarkersBeyondWhenAlsoBoostDatedSnippets()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "research/release", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "research", "release", "state.md"), """
            ---
            title: Release Window
            ---

            ## Current Objective

            Lock the release window for the rollout milestone.

            ## Notes

            - The latest release rollout milestone shipped on 2026-04-12.
            - Earlier milestones did not include the audit log.
            """);

        var results = await searchService.SearchAsync(temp, "latest release rollout milestone", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Contains(results[0].ScoreBreakdown.Keys, key =>
            key == "normalized_date_match" || key == "exact_date_match");
    }

    [Fact]
    public async Task SearchHonorsScopeFilter()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/in-scope", CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "research/out-of-scope", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "projects", "in-scope", "state.md"), """
            ---
            title: In Scope
            ---

            ## Current Objective

            Find the deployment automation guardrail document.
            """);
        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "research", "out-of-scope", "state.md"), """
            ---
            title: Out Of Scope
            ---

            ## Current Objective

            Investigate the same deployment automation guardrail topic.
            """);

        var scoped = await searchService.SearchAsync(temp, "deployment automation guardrail", CancellationToken.None, scope: "projects/");
        var unscoped = await searchService.SearchAsync(temp, "deployment automation guardrail", CancellationToken.None);

        Assert.All(scoped, item => Assert.StartsWith("projects/", item.RelativePath, StringComparison.Ordinal));
        Assert.True(unscoped.Count > scoped.Count);
    }

    [Fact]
    public async Task SearchTieBreakPrefersStateOverDecisions()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/tie-break", CancellationToken.None);
        var nodeRoot = Path.Combine(temp, ".fractal-memory", "projects", "tie-break");

        const string sharedSnippet = "## Notes\n\nCanonical phrase tie break marker line for the test.\n";
        await File.WriteAllTextAsync(Path.Combine(nodeRoot, "state.md"), $"""
            ---
            title: Tie Break State
            ---

            {sharedSnippet}
            """);
        await File.WriteAllTextAsync(Path.Combine(nodeRoot, "decisions.md"), $"""
            ---
            title: Tie Break Decisions
            ---

            {sharedSnippet}
            """);

        var results = await searchService.SearchAsync(temp, "canonical phrase tie break marker", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal("state.md", results[0].MatchedFile);
    }

    [Fact]
    public async Task NumericDateFormatsAreRecognized()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "research/numeric-date", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "research", "numeric-date", "state.md"), """
            ---
            title: Numeric Date
            ---

            ## Current Objective

            Catalog the rollout milestone schedule.

            ## Notes

            - The rollout milestone shipped on 2026/04/12.
            """);

        var results = await searchService.SearchAsync(temp, "when did the rollout milestone ship", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Contains(results[0].ScoreBreakdown.Keys, key =>
            key == "normalized_date_match" || key == "exact_date_match");
    }

    [Fact]
    public async Task OnDiskCacheServesColdReads()
    {
        var workingDirectory = TestEnvironment.CreateTempDirectory();

        using (var primingProvider = TestEnvironment.CreateServices())
        {
            var repositoryService = primingProvider.GetRequiredService<IRepositoryService>();
            var nodeService = primingProvider.GetRequiredService<INodeService>();
            var indexService = primingProvider.GetRequiredService<IIndexService>();

            await repositoryService.InitializeAsync(workingDirectory, CancellationToken.None);
            await nodeService.CreateNodeAsync(workingDirectory, "projects/cold-start", CancellationToken.None);
            await indexService.RefreshAsync(workingDirectory, CancellationToken.None);
        }

        using var coldProvider = TestEnvironment.CreateServices();
        var coldSearchService = coldProvider.GetRequiredService<ISearchService>();
        var coldResults = await coldSearchService.SearchAsync(workingDirectory, "projects/cold-start", CancellationToken.None);

        Assert.NotEmpty(coldResults);
        Assert.Equal("projects/cold-start", coldResults[0].RelativePath);
    }

    [Fact]
    public void YamlFrontMatterRenderQuotesAliasesContainingSpecialChars()
    {
        var metadata = new NodeMetadata
        {
            Title = "Title: with colon",
            Aliases = ["alpha, beta", "gamma", "with \"quote"],
            Summary = "Summary with ] bracket and # hash.",
        };

        var rendered = FractalMemory.Core.Infrastructure.Parsing.YamlFrontMatterParser.Render(metadata);
        var parsed = new FractalMemory.Core.Infrastructure.Parsing.YamlFrontMatterParser().Parse(rendered + "\n\nBody.");

        Assert.Equal(metadata.Title, parsed.Metadata.Title);
        Assert.Equal(metadata.Summary, parsed.Metadata.Summary);
        Assert.Equal(metadata.Aliases.OrderBy(a => a, StringComparer.Ordinal), parsed.Metadata.Aliases.OrderBy(a => a, StringComparer.Ordinal));
    }

    [Fact]
    public async Task NodeListingCacheInvalidatesWhenNodeIsDeleted()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/keeper", CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/deletable", CancellationToken.None);

        var before = await searchService.SearchAsync(temp, "projects/deletable", CancellationToken.None);
        Assert.Contains(before, item => item.RelativePath == "projects/deletable");

        Directory.Delete(Path.Combine(temp, ".fractal-memory", "projects", "deletable"), recursive: true);

        var after = await searchService.SearchAsync(temp, "projects/deletable", CancellationToken.None);
        Assert.DoesNotContain(after, item => item.RelativePath == "projects/deletable");
    }

    [Fact]
    public async Task SearchScopeNormalizesWindowsSeparators()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "projects/win-sep", CancellationToken.None);
        await nodeService.CreateNodeAsync(temp, "research/win-sep", CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "projects", "win-sep", "state.md"), """
            ---
            title: Win Sep Projects
            ---

            ## Current Objective

            Windows separator scope target line.
            """);
        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "research", "win-sep", "state.md"), """
            ---
            title: Win Sep Research
            ---

            ## Current Objective

            Windows separator scope target line.
            """);

        var results = await searchService.SearchAsync(temp, "windows separator scope target", CancellationToken.None, scope: "projects\\");

        Assert.NotEmpty(results);
        Assert.All(results, item => Assert.StartsWith("projects/", item.RelativePath, StringComparison.Ordinal));
    }

    [Fact]
    public void YamlFrontMatterQuotesReservedLiteralsAndNumericStrings()
    {
        var metadata = new NodeMetadata
        {
            Title = "yes",
            Aliases = ["true", "42", "1.5", "n"],
            Summary = "null",
        };

        var rendered = FractalMemory.Core.Infrastructure.Parsing.YamlFrontMatterParser.Render(metadata);
        var parsed = new FractalMemory.Core.Infrastructure.Parsing.YamlFrontMatterParser().Parse(rendered + "\n\nBody.");

        Assert.Equal("yes", parsed.Metadata.Title);
        Assert.Equal("null", parsed.Metadata.Summary);
        Assert.Equal(metadata.Aliases.OrderBy(a => a, StringComparer.Ordinal), parsed.Metadata.Aliases.OrderBy(a => a, StringComparer.Ordinal));
    }

    [Fact]
    public async Task OnDiskCacheRejectsManifestWithPathTraversalEntry()
    {
        var workingDirectory = TestEnvironment.CreateTempDirectory();

        using (var primingProvider = TestEnvironment.CreateServices())
        {
            var repositoryService = primingProvider.GetRequiredService<IRepositoryService>();
            var nodeService = primingProvider.GetRequiredService<INodeService>();
            var indexService = primingProvider.GetRequiredService<IIndexService>();

            await repositoryService.InitializeAsync(workingDirectory, CancellationToken.None);
            await nodeService.CreateNodeAsync(workingDirectory, "projects/traversal", CancellationToken.None);
            await indexService.RefreshAsync(workingDirectory, CancellationToken.None);
        }

        var manifestPath = Path.Combine(workingDirectory, ".fractal-memory", "indexes", "cache", "manifest.json");
        var manifestText = await File.ReadAllTextAsync(manifestPath);
        var tampered = manifestText.Replace("projects_traversal.json", "../../../etc/passwd");
        await File.WriteAllTextAsync(manifestPath, tampered);

        using var coldProvider = TestEnvironment.CreateServices();
        var coldSearchService = coldProvider.GetRequiredService<ISearchService>();
        var results = await coldSearchService.SearchAsync(workingDirectory, "projects/traversal", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Contains(results, item => item.RelativePath == "projects/traversal");
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
