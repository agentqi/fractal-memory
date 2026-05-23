using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Domain.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

public sealed class RepositoryWorkflowTests
{
    [Fact]
    public async Task InitializeCreatesExpectedStructure()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(temp, ".fractal-memory", "config.yaml")));
        Assert.True(File.Exists(Path.Combine(temp, ".fractal-memory", "root", "index.md")));
        Assert.True(Directory.Exists(Path.Combine(temp, ".fractal-memory", "projects")));
        Assert.True(File.Exists(Path.Combine(temp, ".fractal-memory", "templates", "node", "state.md")));
        Assert.True(File.Exists(Path.Combine(temp, ".fractal-memory", "indexes", "paths.yaml")));
    }

    [Fact]
    public async Task CreateNodeBuildsStandardLayout()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        var node = await nodeService.CreateNodeAsync(temp, @"projects\flowone/children/frontend", CancellationToken.None);

        Assert.Equal("projects/flowone/children/frontend", node.RelativePath);
        Assert.True(File.Exists(Path.Combine(temp, ".fractal-memory", "projects", "flowone", "children", "frontend", "index.md")));
        Assert.True(File.Exists(Path.Combine(temp, ".fractal-memory", "projects", "flowone", "children", "frontend", "state.md")));
        Assert.True(Directory.Exists(Path.Combine(temp, ".fractal-memory", "projects", "flowone", "children", "frontend", "children")));
        Assert.True(Directory.Exists(Path.Combine(temp, ".fractal-memory", "projects", "flowone", "children", "frontend", "artifacts")));
    }

    [Fact]
    public async Task CreateNodeCanUseHtmlNodeFiles()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var readService = provider.GetRequiredService<IReadService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var validationService = provider.GetRequiredService<IValidationService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        var node = await nodeService.CreateNodeAsync(temp, "projects/html-node", CancellationToken.None, NodeFileFormat.Html);
        var nodeRoot = Path.Combine(temp, ".fractal-memory", "projects", "html-node");
        await File.WriteAllTextAsync(Path.Combine(nodeRoot, "state.html"), """
            <!doctype html>
            <html lang="en">
            <head><title>HTML Node State</title></head>
            <body>
              <h1>Current State</h1>
              <p>The launch dashboard uses first-class HTML node files.</p>
            </body>
            </html>
            """);

        var opened = await readService.OpenAsync(temp, "projects/html-node", RetrievalDepth.Working, NodeViewType.Index, CancellationToken.None);
        var results = await searchService.SearchAsync(temp, "first-class HTML node files", CancellationToken.None);
        var report = await validationService.ValidateAsync(temp, CancellationToken.None);

        Assert.Equal("index.html", node.IndexFileName);
        Assert.True(File.Exists(Path.Combine(nodeRoot, "index.html")));
        Assert.True(File.Exists(Path.Combine(nodeRoot, "state.html")));
        Assert.Contains("launch dashboard", opened.CurrentState, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("state.html", results[0].MatchedFile);
        Assert.DoesNotContain(report.Issues, issue => issue.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task CreateNodeUsesRepositoryBackedTemplatesWhenPresent()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(temp, ".fractal-memory", "templates", "node", "index.md"), """
            ---
            title: {{TITLE}}
            summary: Custom template for {{TITLE}}.
            ---

            # Custom {{TITLE}}
            """);

        await nodeService.CreateNodeAsync(temp, "projects/custom-template", CancellationToken.None);

        var indexPath = Path.Combine(temp, ".fractal-memory", "projects", "custom-template", "index.md");
        var markdown = await File.ReadAllTextAsync(indexPath);
        Assert.Contains("Custom Custom Template", markdown, StringComparison.Ordinal);
        Assert.Contains("Custom template for Custom Template.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void PathRulesNormalizeAndRejectInvalidInput()
    {
        var normalized = NodePathRules.Normalize(@"projects\flow-one//children/frontend");
        Assert.Equal("projects/flow-one/children/frontend", normalized);
        Assert.Throws<InvalidOperationException>(() => NodePathRules.Normalize("Projects/Bad Node"));
    }
}
