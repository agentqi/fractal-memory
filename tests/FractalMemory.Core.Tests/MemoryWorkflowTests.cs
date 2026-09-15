using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Infrastructure.Parsing;

namespace FractalMemory.Core.Tests;

public sealed class MemoryWorkflowTests
{
    private const string Path = "projects/test";

    [Fact]
    public async Task UpdatePreservesUnknownMetadataAndSectionsAndRejectsStaleHash()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        await repo.WriteAsync($"{Path}/state.md", "---\ncustom:\n  nested: retained\n---\n# State\n\n## Current Objective\n\nOld\n\n## Notes\n\nKeep this.\n");
        var workflow = repo.Get<IMemoryWorkflowService>();
        var original = await workflow.ReadAsync(repo.Root, Path, "state", null, repo.Token);
        var result = await workflow.SetSectionAsync(repo.Root, Path, "state", "Current Objective", "Ship a release.", original.Hash, repo.Token);
        Assert.Contains("nested: retained", result.Document.Content, StringComparison.Ordinal);
        Assert.Contains("Keep this.", result.Document.Content, StringComparison.Ordinal);
        Assert.Contains("last_updated:", result.Document.Content, StringComparison.Ordinal);
        Assert.NotEqual(original.Hash, result.Document.Hash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.SetSectionAsync(repo.Root, Path, "state", "Current Objective", "Lost update", original.Hash, repo.Token));
        Assert.DoesNotContain("Lost update", await repo.ReadAsync($"{Path}/state.md"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentWritersAllowExactlyOneCommitForTheSameHash()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        var workflow = repo.Get<IMemoryWorkflowService>();
        var original = await workflow.ReadAsync(repo.Root, Path, "state", null, repo.Token);
        async Task<bool> Write(string text)
        {
            try { await workflow.SetSectionAsync(repo.Root, Path, "state", "Current Objective", text, original.Hash, repo.Token); return true; }
            catch (InvalidOperationException exception) when (exception.Message.Contains("changed since", StringComparison.Ordinal)) { return false; }
        }
        var results = await Task.WhenAll(Write("First writer"), Write("Second writer"));
        Assert.Single(results, success => success);
    }

    [Fact]
    public async Task SectionReadsUseActualSourceLinesAndIgnoreFencedHeadings()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        const string source = "---\ntitle: Test\n---\n# State\n## Current Objective\nActual objective\n```md\n## Fake\n```\n## Notes\nOther section\n";
        await repo.WriteAsync($"{Path}/state.md", source);
        var workflow = repo.Get<IMemoryWorkflowService>();
        var section = await workflow.ReadAsync(repo.Root, Path, "state", "Current Objective", repo.Token);
        Assert.Equal(6, section.StartLine);
        Assert.Equal(9, section.EndLine);
        Assert.Contains("## Fake", section.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Other section", section.Content, StringComparison.Ordinal);
        Assert.Equal(MemoryMarkdown.Hash(source), section.Hash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ReadAsync(repo.Root, Path, "state", "Fake", repo.Token));
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.SetSectionAsync(repo.Root, Path, "state", "Current Objective", "## Injected sibling", section.Hash, repo.Token));
    }

    [Fact]
    public async Task DuplicateHeadingsCannotBeSilentlyOverwritten()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        await repo.WriteAsync($"{Path}/state.md", "## Notes\nfirst\n## Notes\nsecond\n");
        var workflow = repo.Get<IMemoryWorkflowService>();
        var original = await workflow.ReadAsync(repo.Root, Path, "state", null, repo.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.SetSectionAsync(repo.Root, Path, "state", "Notes", "ambiguous", original.Hash, repo.Token));
    }

    [Theory]
    [InlineData("../config.yaml")]
    [InlineData("artifacts/../../state.md")]
    [InlineData("artifacts/../state.md")]
    [InlineData("/etc/passwd")]
    [InlineData("children/other/state.md")]
    public async Task SourceReadsRejectTraversalAndNonSourcePaths(string file)
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => repo.Get<IMemoryWorkflowService>().ReadAsync(repo.Root, Path, file, null, repo.Token));
    }

    [Fact]
    public async Task ArtifactSymlinksAreRejected()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        File.CreateSymbolicLink(repo.FilePath($"{Path}/artifacts/escape.md"), repo.FilePath("root/state.md"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.Get<IMemoryWorkflowService>().ReadAsync(repo.Root, Path, "artifacts/escape.md", null, repo.Token));
    }

    [Fact]
    public async Task HtmlSourcesAreReadableWithNoFabricatedLineCoordinates()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync(format: NodeFileFormat.Html);
        var workflow = repo.Get<IMemoryWorkflowService>();
        var section = await workflow.ReadAsync(repo.Root, Path, "state", "Current Objective", repo.Token);
        Assert.Null(section.StartLine); Assert.Null(section.EndLine);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.SetSectionAsync(repo.Root, Path, "state", "Current Objective", "Update", section.Hash, repo.Token));
    }

    [Fact]
    public async Task DecisionReplacementPreservesHistoryAndExcludesSupersededAnswerContext()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        var workflow = repo.Get<IMemoryWorkflowService>();
        var original = await workflow.ReadAsync(repo.Root, Path, "decisions", null, repo.Token);
        var first = await workflow.AppendAsync(repo.Root, Path, "decisions", "Use the old storage format.", original.Hash, null, repo.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.SetSectionAsync(repo.Root, Path, "decisions", $"Decision {first.DecisionId}", "Accidentally remove managed metadata", first.Document.Hash, repo.Token));
        var second = await workflow.AppendAsync(repo.Root, Path, "decisions", "Use the new storage format.", first.Document.Hash, first.DecisionId, repo.Token);
        var active = await workflow.DecisionsAsync(repo.Root, Path, false, repo.Token);
        Assert.Equal(second.DecisionId, Assert.Single(active).Id);
        var all = await workflow.DecisionsAsync(repo.Root, Path, true, repo.Token);
        Assert.Equal(2, all.Count);
        Assert.Equal("superseded", all[0].Status);
        var node = await repo.Get<INodeService>().GetNodeAsync(repo.Root, Path, repo.Token);
        var answer = repo.Get<IStructuredMemoryService>().BuildAnswerContext(node, [], 3, 10);
        Assert.Equal("Use the new storage format.", Assert.Single(answer.KeyPriorDecisions));
        var pack = await workflow.ContextAsync(repo.Root, Path, 6000, null, repo.Token);
        Assert.DoesNotContain("Use the old storage format.", pack.Text, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.AppendAsync(repo.Root, Path, "decisions", "Invalid replacement", second.Document.Hash, first.DecisionId, repo.Token));
    }

    [Fact]
    public async Task ScaffoldGuidanceIsMissingKnowledgeAndArchivedNodesDoNotDemandAttention()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        var workflow = repo.Get<IMemoryWorkflowService>();
        var attention = await workflow.AttentionAsync(repo.Root, Path, 30, repo.Token);
        Assert.Contains("Missing Current Objective.", Assert.Single(attention).Reasons);
        Assert.Contains("Missing Key Prior Decision.", attention[0].Reasons);
        var original = await workflow.ReadAsync(repo.Root, Path, "index", null, repo.Token);
        await workflow.ReviewAsync(repo.Root, Path, new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero), NodeStatus.Archived, original.Hash, repo.Token);
        Assert.Empty(await workflow.ListAsync(repo.Root, Path, false, repo.Token));
        Assert.Empty(await workflow.AttentionAsync(repo.Root, Path, 30, repo.Token));
        Assert.Equal(NodeStatus.Archived, Assert.Single(await workflow.ListAsync(repo.Root, Path, true, repo.Token)).Status);
    }

    [Fact]
    public async Task ReviewDatesAndUpdatesSurviveIndexRoundTrip()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        var workflow = repo.Get<IMemoryWorkflowService>();
        var original = await workflow.ReadAsync(repo.Root, Path, "index", null, repo.Token);
        var date = new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero);
        await workflow.ReviewAsync(repo.Root, Path, date, null, original.Hash, repo.Token);
        await repo.Get<IIndexService>().RefreshAsync(repo.Root, repo.Token);
        using var reloaded = TestEnvironment.CreateServices();
        var service = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IMemoryWorkflowService>(reloaded);
        Assert.Equal(date, Assert.Single(await service.ListAsync(repo.Root, Path, false, repo.Token)).ReviewAfter);
        await repo.ReplaceAsync($"{Path}/index.md", date.ToString("O"), "2026-01-01T00:00:00.0000000+00:00");
        Assert.Contains((await workflow.AttentionAsync(repo.Root, Path, 30, repo.Token))[0].Reasons, reason => reason.StartsWith("Review overdue", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(256)]
    [InlineData(700)]
    [InlineData(6000)]
    public async Task ContextRespectsItsBudgetAndReportsTruncation(int budget)
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        await repo.WriteAsync($"{Path}/state.md", "## Current Objective\nShip the product.\n## Active Constraints\n" + string.Concat(Enumerable.Repeat("Keep data local. 🌲", 500)));
        var pack = await repo.Get<IMemoryWorkflowService>().ContextAsync(repo.Root, Path, budget, null, repo.Token);
        Assert.InRange(pack.Text.Length, 1, budget);
        Assert.Equal(pack.Text.Length, pack.UsedCharacters);
        Assert.True(pack.Truncated);
        Assert.NotEmpty(pack.OmittedSections);
        Assert.All(pack.Sources, source => Assert.StartsWith("memory://document/", source.ResourceUri));
        Assert.DoesNotContain('\uFFFD', pack.Text);
    }

    [Fact]
    public async Task ResumeIsNodeScopedAndComparesSourceSnapshots()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        await repo.CreateNodeAsync("projects/test-other");
        var workflow = repo.Get<IMemoryWorkflowService>();
        var handoff = await repo.Get<IHandoffService>().CreateAsync(repo.Root, Path, repo.Token);
        await repo.Get<IHandoffService>().CreateAsync(repo.Root, "projects/test-other", repo.Token);
        Assert.Single(await workflow.HandoffsAsync(repo.Root, Path, repo.Token));
        var before = await workflow.ResumeAsync(repo.Root, Path, 6000, repo.Token);
        Assert.True(before.ComparisonAvailable); Assert.Empty(before.ChangedFiles);
        var state = await workflow.ReadAsync(repo.Root, Path, "state", null, repo.Token);
        await workflow.SetSectionAsync(repo.Root, Path, "state", "Current Objective", "Continue from the handoff.", state.Hash, repo.Token);
        var after = await workflow.ResumeAsync(repo.Root, Path, 6000, repo.Token);
        Assert.Equal("state.md", Assert.Single(after.ChangedFiles));
        Assert.EndsWith(after.LatestHandoff!.File, handoff.HandoffFilePath, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ReadHandoffAsync(repo.Root, Path, "../config.yaml", repo.Token));
    }

    [Fact]
    public async Task ImportPreviewPreservesOriginalAndBlocksConflictsAndDuplicates()
    {
        using var repo = await RegressionRepository.CreateAsync();
        var workflow = repo.Get<IMemoryWorkflowService>();
        const string source = "# Original\r\n\r\nKeep exact line endings and trailing whitespace.  ";
        var modified = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var preview = await workflow.ImportAsync(repo.Root, Path, "old-notes.md", source, modified, false, repo.Token);
        Assert.True(preview.CanApply); Assert.False(preview.Applied);
        Assert.False(Directory.Exists(repo.FilePath(Path)));
        var imported = await workflow.ImportAsync(repo.Root, Path, "old-notes.md", source, modified, true, repo.Token);
        Assert.True(imported.Applied);
        Assert.Equal(source, await repo.ReadAsync($"{Path}/artifacts/source.md"));
        var index = await repo.ReadAsync($"{Path}/index.md");
        Assert.Contains(modified.ToString("O"), index, StringComparison.Ordinal);
        Assert.False((await workflow.ImportAsync(repo.Root, Path, "different.md", "Different content", null, true, repo.Token)).CanApply);
        var duplicate = await workflow.ImportAsync(repo.Root, "projects/duplicate", "copy.md", source, null, true, repo.Token);
        Assert.False(duplicate.Applied); Assert.Equal(Path, Assert.Single(duplicate.DuplicateNodes));
        Assert.False(Directory.Exists(repo.FilePath("projects/duplicate")));
        var pack = await workflow.ContextAsync(repo.Root, Path, 6000, ["artifacts/source.md"], repo.Token);
        Assert.Contains("Keep exact line endings", pack.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorRebuildsIndexesWithoutChangingSourcesAndExplainsBadConfig()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        var workflow = repo.Get<IMemoryWorkflowService>();
        var source = await repo.ReadAsync($"{Path}/state.md");
        var doctor = await workflow.DoctorAsync(repo.Root, true, repo.Token);
        Assert.True(doctor.IndexesRebuilt);
        Assert.False(doctor.Validation.HasErrors);
        Assert.Equal(source, await repo.ReadAsync($"{Path}/state.md"));
        await repo.WriteAsync("config.yaml", "default_depth: nonsense");
        var broken = await workflow.DoctorAsync(repo.Root, true, repo.Token);
        Assert.True(broken.Validation.HasErrors); Assert.False(broken.IndexesRebuilt);
    }
    [Theory]
    [InlineData("default_depth: nonsense")]
    [InlineData("indexing: {enabled: nonsense}")]
    [InlineData("retrieval: invalid-section")]
    [InlineData("retrieval: {answer_top_k: null}")]
    [InlineData("metadata: [broken")]
    public async Task DoctorExplainsInvalidConfiguration(string config)
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.WriteAsync("config.yaml", config);
        var report = await repo.Get<IMemoryWorkflowService>().DoctorAsync(repo.Root, false, repo.Token);
        Assert.True(report.Validation.HasErrors);
        Assert.False(report.IndexesRebuilt);
    }

    [Fact]
    public void SharedHeadingsIgnoreCommentsInsideCodeAndPreserveHashCharacters()
    {
        const string source = "# C#\nCode notes\n```html\n<!-- unclosed example\n```\n## Real section ###\nContent\n";
        var headings = MemoryMarkdown.Headings(source);
        Assert.Equal(["C#", "Real section"], headings.Select(h => h.Title));
        var updated = MemoryMarkdown.SetMetadata("---  \ncustom: keep\n--- \n## Notes\nBody\n", new Dictionary<string, object?> { ["review_after"] = "2027-01-01" });
        Assert.Equal("keep", MemoryMarkdown.Metadata(updated)["custom"]);
        Assert.Single(MemoryMarkdown.Headings(updated));
    }

    [Fact]
    public async Task LegacyHandoffExplicitlyReportsUnavailableComparison()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        await repo.WriteAsync("handoffs/legacy.md", $"# Legacy\n## Read First Files\n- `{Path}/state.md`\n");
        var resume = await repo.Get<IMemoryWorkflowService>().ResumeAsync(repo.Root, Path, 6000, repo.Token);
        Assert.Equal("legacy.md", resume.LatestHandoff!.File);
        Assert.False(resume.ComparisonAvailable);
    }

    [Fact]
    public async Task SimultaneousImportsDoNotCreateDuplicateNodes()
    {
        using var repo = await RegressionRepository.CreateAsync();
        var workflow = repo.Get<IMemoryWorkflowService>();
        var imports = await Task.WhenAll(
            workflow.ImportAsync(repo.Root, "research/first", "note.md", "# Identical source", null, true, repo.Token),
            workflow.ImportAsync(repo.Root, "research/second", "note.md", "# Identical source", null, true, repo.Token));
        Assert.Single(imports, result => result.Applied);
        Assert.Single(imports, result => result.DuplicateNodes.Count == 1);
    }

}
