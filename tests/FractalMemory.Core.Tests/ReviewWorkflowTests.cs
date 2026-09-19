using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Infrastructure.Parsing;

namespace FractalMemory.Core.Tests;

public sealed class ReviewWorkflowTests
{
    private const string Node = "projects/test";

    [Theory]
    [InlineData("heading")]
    [InlineData("json")]
    [InlineData("duplicate")]
    [InlineData("marker")]
    public async Task DamagedManagedDecisionsRemainReadableButBlockWritesAndDoctorRepair(string damage)
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        var workflow = repo.Get<IMemoryWorkflowService>();
        var source = await workflow.ReadAsync(repo.Root, Node, "decisions", null, repo.Token);
        var written = await workflow.AppendAsync(repo.Root, Node, "decisions", "- Keep an audit trail.", source.Hash, null, repo.Token);
        var text = written.Document.Content;
        text = damage switch
        {
            "heading" => text.Replace("## Decision ", "## Renamed ", StringComparison.Ordinal),
            "json" => text.Replace("\"Status\":\"active\"", "\"Status\":BROKEN", StringComparison.Ordinal),
            "duplicate" => text + text[text.IndexOf("## Decision ", StringComparison.Ordinal)..],
            _ => text.Replace(" -->", " broken -->", StringComparison.Ordinal),
        };
        await repo.WriteAsync($"{Node}/decisions.md", text);
        await repo.Get<IIndexService>().RefreshAsync(repo.Root, repo.Token);
        var exported = await repo.Get<IExportService>().ExportAsync(repo.Root, Node, ExportMode.Standard, repo.Token);
        Assert.NotNull(exported.AnswerContext);
        var context = await workflow.ContextAsync(repo.Root, Node, 6000, null, repo.Token);
        Assert.Contains("Keep an audit trail.", context.Text, StringComparison.Ordinal);
        Assert.Contains(context.Warnings, w => w.Contains("metadata needs repair", StringComparison.Ordinal));
        var doctor = await workflow.DoctorAsync(repo.Root, true, repo.Token);
        Assert.True(doctor.Validation.HasErrors);
        Assert.False(doctor.IndexesRebuilt);
        Assert.Contains(doctor.Validation.Issues, i => i.Message.Contains("decisions.md", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.AppendAsync(repo.Root, Node, "decisions", "Do not overwrite damage", MemoryMarkdown.Hash(text), null, repo.Token));
        Assert.Equal(text, await repo.ReadAsync($"{Node}/decisions.md"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextAndExportPreserveCuratedDecisionsWithActiveOrSupersededLog(bool superseded)
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        await repo.WriteAsync($"{Node}/state.md", "---\ntitle: State\n---\n---\n## Current Goal\nShip safely.\n## Key Decisions In Force\n- Keep SQLite.\n## Constraint\n- Preserve existing data.\n## Next Action\n- Run a canary.\n## Project / Branch\nrelease/v1\n");
        var workflow = repo.Get<IMemoryWorkflowService>();
        var source = await workflow.ReadAsync(repo.Root, Node, "decisions", null, repo.Token);
        var written = await workflow.AppendAsync(repo.Root, Node, "decisions", "Ship weekly.", source.Hash, null, repo.Token);
        if (superseded) await repo.WriteAsync($"{Node}/decisions.md", written.Document.Content.Replace("\"active\"", "\"superseded\"", StringComparison.Ordinal));
        var exported = await repo.Get<IExportService>().ExportAsync(repo.Root, Node, ExportMode.Standard, repo.Token);
        Assert.Equal("Ship safely.", exported.AnswerContext!.CurrentObjective);
        Assert.Contains("Keep SQLite.", exported.AnswerContext.KeyPriorDecisions);
        Assert.Equal(!superseded, exported.AnswerContext.KeyPriorDecisions.Contains("Ship weekly."));
        var context = await workflow.ContextAsync(repo.Root, Node, 6000, null, repo.Token);
        foreach (var expected in new[] { "Ship safely.", "Keep SQLite.", "Preserve existing data.", "Run a canary.", "release/v1" })
            Assert.Contains(expected, context.Text, StringComparison.Ordinal);
        Assert.Equal(!superseded, context.Text.Contains("Ship weekly.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MeaningfulSummarySuppliesObjectiveButScaffoldDoesNot()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        var workflow = repo.Get<IMemoryWorkflowService>();
        var empty = await workflow.ContextAsync(repo.Root, Node, 6000, null, repo.Token);
        Assert.Contains("Missing Current Objective.", empty.Warnings);
        await repo.ReplaceAsync($"{Node}/index.md", "Summary for Test.", "Migrate billing by Q4.");
        var export = await repo.Get<IExportService>().ExportAsync(repo.Root, Node, ExportMode.Standard, repo.Token);
        Assert.Equal("Migrate billing by Q4.", export.AnswerContext!.CurrentObjective);
        var context = await workflow.ContextAsync(repo.Root, Node, 6000, null, repo.Token);
        Assert.DoesNotContain("Missing Current Objective.", context.Warnings);
        Assert.Contains(context.Sources, s => s.File == "index.md" && s.Content.Contains("Migrate billing by Q4.", StringComparison.Ordinal));
    }

    [Fact]
    public void ThematicBreakCannotHideInjectedSiblingHeading()
    {
        const string text = "## Objective\nOld\n## Notes\nKeep\n";
        Assert.Throws<ArgumentException>(() => MemoryMarkdown.ReplaceSection(text, "Objective", "---\n## Injected\nData"));
        var parsed = new StructuredMemoryService().Parse("---\n## Current Objective\nShip safely.\n", "test");
        Assert.Equal("Ship safely.", parsed.CurrentObjective);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("ON", true)]
    [InlineData("no", false)]
    [InlineData("Off", false)]
    public async Task NullConfigSectionsAndLegacyYamlBooleansRetainDefaults(string boolean, bool enabled)
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.WriteAsync("config.yaml", $"handoffs:\nmetadata:\nretrieval:\nvalidation:\nindexing:\n  enabled: {boolean}\n");
        var config = await repo.Get<IRepositoryService>().LoadConfigAsync(repo.Root, repo.Token);
        Assert.Equal(enabled, config.Indexing.Enabled);
        Assert.Equal("handoffs", config.Handoffs.Directory);
        Assert.True(config.Metadata.FrontMatter);
    }

    [Fact]
    public async Task GeneratedFilesHaveNewlineAndMarkedCustomGuidanceIsNeverKnowledge()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.ReplaceAsync("templates/node/state.md", "Capture what is currently true, active, and important.", "Custom template instructions belong here.");
        await repo.CreateNodeAsync();
        foreach (var name in new[] { "index", "state", "decisions", "timeline" })
            Assert.EndsWith("\n", await repo.ReadAsync($"{Node}/{name}.md"), StringComparison.Ordinal);
        var workflow = repo.Get<IMemoryWorkflowService>();
        var context = await workflow.ContextAsync(repo.Root, Node, 6000, null, repo.Token);
        Assert.DoesNotContain("Custom template instructions", context.Text, StringComparison.Ordinal);
        Assert.Contains("Missing Current Objective.", context.Warnings);
        const string original = "source without a final newline";
        Assert.True((await workflow.ImportAsync(repo.Root, "projects/import", "note.txt", original, null, true, repo.Token)).Applied);
        Assert.Equal(original, await repo.ReadAsync("projects/import/artifacts/source.txt"));
    }

    [Fact]
    public async Task ResumeDoesNotReadMalformedDescendantNodes()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        await repo.CreateNodeAsync("projects/test/children/broken");
        await repo.WriteAsync("projects/test/children/broken/state.md", "---\ntitle: [broken\n---\nBad YAML\n");
        var resume = await repo.Get<IMemoryWorkflowService>().ResumeAsync(repo.Root, Node, 6000, repo.Token);
        Assert.Equal(Node, resume.Context.NodePath);
    }
}
