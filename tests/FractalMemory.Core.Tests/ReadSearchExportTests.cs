using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

public sealed class ReadSearchExportTests
{
    [Fact]
    public async Task OpenRespectsDepthBehavior()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var readService = provider.GetRequiredService<IReadService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await nodeService.CreateNodeAsync(temp, "projects/fractal-memory-cli", TestContext.Current.CancellationToken);
        var nodeRoot = Path.Combine(temp, ".fractal-memory", "projects", "fractal-memory-cli");
        await TestFile.WriteAllTextAsync(Path.Combine(nodeRoot, "index.md"), """
            ---
            title: Fractal Memory CLI
            summary: Build the structured memory CLI.
            ---

            # Fractal Memory CLI

            Overview paragraph.
            """);
        await TestFile.WriteAllTextAsync(Path.Combine(nodeRoot, "state.md"), """
            ---
            title: Fractal Memory CLI State
            ---

            Current work is implementing the MVP command set.
            Open question? settle export defaults.
            """);
        await TestFile.WriteAllTextAsync(Path.Combine(nodeRoot, "timeline.md"), """
            ---
            title: Timeline
            ---

            - 2026-04-01: Designed the repository layout.
            - 2026-04-02: Implemented the command surface.
            """);
        await TestFile.WriteAllTextAsync(Path.Combine(nodeRoot, "decisions.md"), """
            ---
            title: Decisions
            ---

            - Prefer filesystem truth over indexes.
            """);

        var orientation = await readService.OpenAsync(temp, "projects/fractal-memory-cli", RetrievalDepth.Orientation, NodeViewType.Index, TestContext.Current.CancellationToken);
        var deep = await readService.OpenAsync(temp, "projects/fractal-memory-cli", RetrievalDepth.Deep, NodeViewType.Index, TestContext.Current.CancellationToken);

        Assert.Null(orientation.CurrentState);
        Assert.NotNull(deep.CurrentState);
        Assert.NotEmpty(deep.RecentTimeline);
        Assert.NotEmpty(deep.RecentDecisions);
    }

    [Fact]
    public async Task SearchRanksExactPathAboveContent()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await nodeService.CreateNodeAsync(temp, "projects/alpha", TestContext.Current.CancellationToken);
        await nodeService.CreateNodeAsync(temp, "research/notes", TestContext.Current.CancellationToken);
        await TestFile.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "research", "notes", "state.md"), """
            ---
            title: Notes
            ---

            projects/alpha is referenced in content only.
            """);

        var results = await searchService.SearchAsync(temp, "projects/alpha", TestContext.Current.CancellationToken);

        Assert.NotEmpty(results);
        Assert.Equal("projects/alpha", results[0].RelativePath);
        Assert.Equal("path", results[0].MatchedFile);
    }

    [Fact]
    public async Task SearchIndexesHtmlArtifactsAsReadableText()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await nodeService.CreateNodeAsync(temp, "projects/html-artifacts", TestContext.Current.CancellationToken);
        var artifactsRoot = Path.Combine(temp, ".fractal-memory", "projects", "html-artifacts", "artifacts");
        await TestFile.WriteAllTextAsync(Path.Combine(artifactsRoot, "design.html"), """
            <!doctype html>
            <html>
              <head>
                <title>Design Notes</title>
                <style>.hidden { display: none; }</style>
              </head>
              <body>
                <h1>Launch Architecture</h1>
                <p>The signup funnel uses the aurora onboarding prototype.</p>
                <script>const secret = "ignore script content";</script>
              </body>
            </html>
            """);

        var results = await searchService.SearchAsync(temp, "aurora onboarding prototype", TestContext.Current.CancellationToken);

        Assert.NotEmpty(results);
        Assert.Equal("projects/html-artifacts", results[0].RelativePath);
        Assert.Equal("artifacts/design.html", results[0].MatchedFile);
        Assert.Contains("signup funnel", results[0].Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ignore script content", results[0].Snippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenSplitsParagraphsAcrossCrLfContent()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var readService = provider.GetRequiredService<IReadService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await nodeService.CreateNodeAsync(temp, "projects/crlf", TestContext.Current.CancellationToken);
        var nodeRoot = Path.Combine(temp, ".fractal-memory", "projects", "crlf");
        await TestFile.WriteAllTextAsync(Path.Combine(nodeRoot, "state.md"), "---\r\ntitle: CRLF State\r\n---\r\n\r\nFirst paragraph.\r\n\r\nSecond paragraph.\r\n\r\nThird paragraph.\r\n");

        var opened = await readService.OpenAsync(temp, "projects/crlf", RetrievalDepth.Working, NodeViewType.Index, TestContext.Current.CancellationToken);

        Assert.NotNull(opened.CurrentState);
        Assert.Contains("First paragraph.", opened.CurrentState, StringComparison.Ordinal);
        Assert.Contains("Second paragraph.", opened.CurrentState, StringComparison.Ordinal);
        Assert.DoesNotContain("Third paragraph.", opened.CurrentState, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecentUsesInjectedClock()
    {
        using var provider = TestEnvironment.CreateServices(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await nodeService.CreateNodeAsync(temp, "projects/future-clock", TestContext.Current.CancellationToken);

        var items = await searchService.GetRecentAsync(temp, 10, 30, null, TestContext.Current.CancellationToken);

        Assert.Empty(items);
    }

    [Fact]
    public async Task ExportAndHandoffProduceStructuredOutput()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var exportService = provider.GetRequiredService<IExportService>();
        var handoffService = provider.GetRequiredService<IHandoffService>();
        var aiFormatter = provider.GetRequiredService<IAiExportFormatter>();
        var indexService = provider.GetRequiredService<IIndexService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await nodeService.CreateNodeAsync(temp, "projects/fractal-memory-cli", TestContext.Current.CancellationToken);
        var nodeRoot = Path.Combine(temp, ".fractal-memory", "projects", "fractal-memory-cli");
        await TestFile.WriteAllTextAsync(Path.Combine(nodeRoot, "index.md"), """
            ---
            title: Fractal Memory CLI
            aliases: [fm]
            tags: [cli, memory]
            summary: Build the local-first structured memory CLI.
            ---

            Overview.
            """);
        await TestFile.WriteAllTextAsync(Path.Combine(nodeRoot, "state.md"), """
            ---
            title: Current State
            ---

            Current goal is finishing the MVP.
            What should verbose export include?
            """);
        await TestFile.WriteAllTextAsync(Path.Combine(nodeRoot, "decisions.md"), """
            ---
            title: Decisions
            ---

            - Use markdown files as source of truth.
            """);

        await indexService.RefreshAsync(temp, TestContext.Current.CancellationToken);
        var export = await exportService.ExportAsync(temp, "projects/fractal-memory-cli", ExportMode.Standard, TestContext.Current.CancellationToken);
        var rendered = aiFormatter.Format(export);
        var handoff = await handoffService.CreateAsync(temp, "projects/fractal-memory-cli", TestContext.Current.CancellationToken);

        Assert.Contains("[memory:projects/fractal-memory-cli]", rendered, StringComparison.Ordinal);
        Assert.Contains("decision_highlights:", rendered, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(temp, handoff.HandoffFilePath)));

        var aliases = await TestFile.ReadAllTextAsync(Path.Combine(temp, ".fractal-memory", "indexes", "aliases.yaml"));
        var paths = await TestFile.ReadAllTextAsync(Path.Combine(temp, ".fractal-memory", "indexes", "paths.yaml"));
        Assert.Contains("fm", aliases, StringComparison.Ordinal);
        Assert.Contains("projects/fractal-memory-cli", paths, StringComparison.Ordinal);
    }
}
