using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

public sealed class RetrievalRegressionTests
{
    [Fact]
    public async Task TemplateStateIsReadableAndCitedInOpenExportAndHandoff()
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync();
        await repository.ReplaceAsync("projects/test/state.md", "Capture what is currently true, active, and important.", "Launch the release safely.");
        await repository.ReplaceAsync("projects/test/state.md", "- Add constraints, guardrails, or decisions in force.", "- Never overwrite the production database.");
        await repository.ReplaceAsync("projects/test/state.md", "- Add the next best actions.", "- Run the canary rollout.");
        var read = repository.Get<IReadService>();
        var deep = await read.OpenAsync(repository.Root, "projects/test", RetrievalDepth.Deep, NodeViewType.Index, repository.Token);
        var focused = await read.OpenAsync(repository.Root, "projects/test", RetrievalDepth.Pointer, NodeViewType.State, repository.Token);
        var working = await read.OpenAsync(repository.Root, "projects/test", RetrievalDepth.Working, NodeViewType.Index, repository.Token);
        foreach (var result in new[] { deep, focused, working })
        {
            Assert.Contains("Launch the release safely.", result.CurrentState, StringComparison.Ordinal);
            Assert.Contains("Never overwrite the production database.", result.CurrentState, StringComparison.Ordinal);
            Assert.Contains("Run the canary rollout.", result.CurrentState, StringComparison.Ordinal);
        }
        Assert.False(deep.StateTruncated);
        Assert.False(focused.StateTruncated);
        Assert.True(working.StateTruncated);

        var exported = await repository.Get<IExportService>().ExportAsync(repository.Root, "projects/test", ExportMode.Verbose, repository.Token);
        Assert.Contains("Never overwrite the production database.", exported.CurrentState, StringComparison.Ordinal);
        var stateSource = Assert.Single(exported.AnswerContext!.SupportingSources, source => source.SourcePath.EndsWith("/state.md", StringComparison.Ordinal));
        Assert.Contains("Launch the release safely.", stateSource.Excerpt, StringComparison.Ordinal);
        var handoff = await repository.Get<IHandoffService>().CreateAsync(repository.Root, "projects/test", repository.Token);
        Assert.Contains("Never overwrite the production database.", handoff.CurrentState, StringComparison.Ordinal);
        Assert.Contains(handoff.SourceReferences, source => source.Contains("projects/test/state.md", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task SearchAndExportCitationsLocateActualSourceLinesIncludingColdCache(string newline)
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync();
        var state = string.Join(newline, new[]
        {
            "---", "title: Test State", "last_updated: 2026-09-15T12:00:00Z", "summary: Working state.", "---", "", "",
            "# Current State", "", "## Current Objective", "", "Launch the celadon migration.", "",
            "Keep the rollback ready.", "", "## Active Constraints", "", "- Never overwrite the production database.", "",
        });
        await repository.WriteAsync("projects/test/state.md", state);
        await repository.Get<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        using var cold = TestEnvironment.CreateServices();
        foreach (var services in new[] { repository.Services, cold })
        {
            var hit = Assert.Single(await services.GetRequiredService<ISearchService>().SearchAsync(repository.Root, "overwrite", repository.Token));
            var range = await ReadRange(repository, hit.SourcePath, hit.StartLine, hit.EndLine);
            Assert.Contains("Never overwrite the production database.", range, StringComparison.Ordinal);
            Assert.Equal(hit.Snippet, string.Join(Environment.NewLine, range.Split('\n', StringSplitOptions.RemoveEmptyEntries)).Trim());
            var export = await services.GetRequiredService<IExportService>().ExportAsync(repository.Root, "projects/test", ExportMode.Standard, repository.Token);
            Assert.Contains(export.AnswerContext!.SupportingSources, source => source.SourcePath.EndsWith("/state.md", StringComparison.Ordinal));
            foreach (var source in export.AnswerContext.SupportingSources)
            {
                var cited = await ReadRange(repository, source.SourcePath, source.StartLine, source.EndLine);
                Assert.Equal(source.Excerpt.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(), cited.TrimEnd());
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unrelated preceding line.")]
    public async Task OneLineSnippetAlwaysIncludesTheMatchingLine(string precedingLine)
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync();
        await repository.WriteAsync("projects/test/state.md", $"---\ntitle: State\n---\n\n## Constraints\n{precedingLine}\nNever overwrite the database.\n");
        await repository.ReplaceAsync("config.yaml", "max_snippet_lines: 4", "max_snippet_lines: 1");
        var hit = Assert.Single(await repository.Get<ISearchService>().SearchAsync(repository.Root, "overwrite", repository.Token));
        Assert.Equal("Never overwrite the database.", hit.Snippet);
        Assert.Equal(hit.StartLine, hit.EndLine);
        Assert.Equal(hit.Snippet, await ReadRange(repository, hit.SourcePath, hit.StartLine, hit.EndLine));
    }

    [Fact]
    public async Task ScopedSearchDoesNotParseBrokenNeighborsOrPopulateGlobalCacheWithPartialResults()
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync("projects/good");
        await repository.CreateNodeAsync("projects/good/children/child");
        await repository.CreateNodeAsync("projects/bad");
        await repository.Get<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        var original = await repository.ReadAsync("projects/bad/index.md");
        await repository.WriteAsync("projects/bad/index.md", "---\ntitle: [broken\n---\nBroken\n");
        using var cold = TestEnvironment.CreateServices();
        var search = cold.GetRequiredService<ISearchService>();
        var hits = await search.SearchAsync(repository.Root, "child", repository.Token, scope: "PROJECTS\\GOOD\\");
        Assert.Contains(hits, hit => hit.RelativePath == "projects/good/children/child");
        Assert.All(hits, hit => Assert.StartsWith("projects/good/", hit.RelativePath, StringComparison.Ordinal));
        Assert.Empty(await search.SearchAsync(repository.Root, "anything", repository.Token, scope: "projects/missing"));
        await repository.WriteAsync("projects/bad/index.md", original);
        Assert.Contains(await search.SearchAsync(repository.Root, "projects/bad", repository.Token), hit => hit.RelativePath == "projects/bad");
    }

    [Fact]
    public async Task HtmlRetainsMemoryFieldsAndUsesRealSourcePathsWithoutInventedLineNumbers()
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync("projects/html-demo", NodeFileFormat.Html);
        await repository.WriteAsync("projects/html-demo/state.html", """
            <!doctype html><html><head><title>Unrelated head title</title><style>irrelevant</style></head><body>
            <h1>Current State</h1>
            <section><h2>Project / Branch</h2><p>projects/html-demo</p></section>
            <section><h2>Current Objective</h2><p>Ship the migration.</p></section>
            <section><h2>Active Constraints</h2><ul><li>Preserve <strong>all</strong> customer records.</li></ul></section>
            <section><h2>Next Best Actions</h2><ul><li>Run the canary rollout.</li><li>Check the metrics.</li></ul></section>
            <section><h2>Open Questions</h2><ul><li>Who owns rollback?</li></ul></section>
            <section><h2>Notes about &lt;source&gt;</h2><p>Keep &amp;lt; literally encoded.</p></section>
            <script>Never treat scripts as memory.</script></body></html>
            """);
        await repository.WriteAsync("projects/html-demo/decisions.html", "<h1>Decisions</h1><h2>Key Decisions in Force</h2><ul><li>Use the staged deployment.</li></ul>");
        var export = await repository.Get<IExportService>().ExportAsync(repository.Root, "projects/html-demo", ExportMode.Standard, repository.Token);
        var context = export.AnswerContext!;
        Assert.Equal("Ship the migration.", context.CurrentObjective);
        Assert.Equal("projects/html-demo", context.ProjectBranch);
        Assert.Equal(new[] { "Preserve all customer records." }, context.ActiveConstraints);
        Assert.Equal(new[] { "Run the canary rollout.", "Check the metrics." }, context.NextBestActions);
        Assert.Equal(new[] { "Use the staged deployment." }, context.KeyPriorDecisions);
        Assert.DoesNotContain("Unrelated head title", export.CurrentState, StringComparison.Ordinal);
        Assert.DoesNotContain("scripts", export.CurrentState, StringComparison.Ordinal);
        Assert.Contains("Notes about <source>", export.CurrentState, StringComparison.Ordinal);
        Assert.Contains("Keep &lt; literally encoded.", export.CurrentState, StringComparison.Ordinal);
        Assert.All(context.SupportingSources, source => { Assert.Null(source.StartLine); Assert.Null(source.EndLine); });

        var titleHit = Assert.Single(await repository.Get<ISearchService>().SearchAsync(repository.Root, "Html Demo", repository.Token));
        Assert.Equal("index.html", titleHit.MatchedFile);
        Assert.True(File.Exists(repository.FilePath(titleHit.SourcePath)));
        var contentHit = Assert.Single(await repository.Get<ISearchService>().SearchAsync(repository.Root, "canary rollout", repository.Token));
        Assert.Contains("canary rollout", contentHit.Snippet, StringComparison.Ordinal);
        Assert.Null(contentHit.StartLine);
        Assert.Null(contentHit.EndLine);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task CoreRejectsUndefinedEnumsBeforeDoingWork(int invalid)
    {
        using var repository = await RegressionRepository.CreateAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.Get<INodeService>()
            .CreateNodeAsync(repository.Root, "projects/invalid", repository.Token, (NodeFileFormat)invalid));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.Get<IReadService>()
            .OpenAsync(repository.Root, "root", (RetrievalDepth)invalid, NodeViewType.Index, repository.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.Get<IReadService>()
            .OpenAsync(repository.Root, "root", RetrievalDepth.Orientation, (NodeViewType)invalid, repository.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.Get<IExportService>()
            .ExportAsync(repository.Root, "root", (ExportMode)invalid, repository.Token));
        Assert.False(Directory.Exists(repository.FilePath("projects/invalid")));
    }

    private static async Task<string> ReadRange(RegressionRepository repository, string path, int? start, int? end)
    {
        Assert.NotNull(start);
        Assert.NotNull(end);
        var lines = (await repository.ReadAsync(path)).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        Assert.InRange(start.Value, 1, lines.Length);
        Assert.InRange(end.Value, start.Value, lines.Length);
        return string.Join('\n', lines.Skip(start.Value - 1).Take(end.Value - start.Value + 1));
    }
}
