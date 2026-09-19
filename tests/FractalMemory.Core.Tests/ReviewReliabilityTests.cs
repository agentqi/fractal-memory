using System.Text.Json.Nodes;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Infrastructure.Indexing;

namespace FractalMemory.Core.Tests;

public sealed class ReviewReliabilityTests
{
    [Fact]
    public async Task HtmlInlineWordsRemainSeparateInTitlesAndEvidence()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync(format: NodeFileFormat.Html);
        await repo.WriteAsync("projects/test/index.html", "<h1><span>Billing</span><strong>Migration</strong></h1>");
        await repo.WriteAsync("projects/test/state.html", "<h2>Current Objective</h2><p><strong>Blocker:</strong>Auth rollout</p><table><tr><td>Owner</td><td>Alice</td></tr></table>");
        var node = await repo.Get<INodeService>().GetNodeAsync(repo.Root, "projects/test", repo.Token);
        Assert.Equal("Billing Migration", node.Metadata.Title);
        Assert.Contains("Blocker: Auth rollout", node.StateContent, StringComparison.Ordinal);
        Assert.Contains("Owner Alice", node.StateContent, StringComparison.Ordinal);
        Assert.NotEmpty(await repo.Get<ISearchService>().SearchAsync(repo.Root, "Owner Alice", repo.Token));
    }

    [Theory]
    [InlineData(".staging/unfinished")]
    [InlineData("archive/old")]
    [InlineData("projects/test/artifacts/note")]
    public async Task ReservedDirectoriesStayOutOfDiscoveryRecentAndValidation(string path)
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync();
        Directory.CreateDirectory(repo.FilePath(path));
        await repo.WriteAsync(path + "/index.md", "---\nmalformed: [\n---\n");
        Assert.DoesNotContain(await repo.Get<INodeService>().GetAllNodesAsync(repo.Root, repo.Token), node => node.RelativePath == path);
        Assert.DoesNotContain(await repo.Get<ISearchService>().GetRecentAsync(repo.Root, 50, 365, null, repo.Token), item => item.RelativePath == path);
        Assert.DoesNotContain((await repo.Get<IValidationService>().ValidateAsync(repo.Root, repo.Token)).Issues, issue => issue.RelativePath == path);
    }

    [Theory]
    [InlineData("./projects")]
    [InlineData("projects/my_project")]
    [InlineData("research/v1.2")]
    public async Task ScopesEnforceCanonicalNodeNames(string scope)
    {
        using var repo = await RegressionRepository.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.Get<ISearchService>().SearchAsync(repo.Root, "root", repo.Token, scope: scope));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.Get<ISearchService>().GetRecentAsync(repo.Root, 5, 30, scope, repo.Token));
    }

    [Fact]
    public async Task MissingRefreshSettingDefaultsToTrueAndExplicitFalseGetsMigrationHint()
    {
        using var repo = await RegressionRepository.CreateAsync();
        var config = await repo.ReadAsync("config.yaml");
        await repo.WriteAsync("config.yaml", config.Replace("refresh_on_write: true", "", StringComparison.Ordinal));
        Assert.True((await repo.Get<IRepositoryService>().LoadConfigAsync(repo.Root, repo.Token)).Indexing.RefreshOnWrite);
        await repo.WriteAsync("config.yaml", config.Replace("refresh_on_write: true", "refresh_on_write: false", StringComparison.Ordinal));
        Assert.False((await repo.Get<IRepositoryService>().LoadConfigAsync(repo.Root, repo.Token)).Indexing.RefreshOnWrite);
        File.SetLastWriteTimeUtc(repo.FilePath("root/state.md"), DateTime.UtcNow.AddDays(1));
        var issues = (await repo.Get<IValidationService>().ValidateAsync(repo.Root, repo.Token)).Issues;
        Assert.Contains(issues, issue => issue.Message.Contains("indexing.refresh_on_write: true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoncanonicalManualNodesCanBeCachedWithoutFilenameCollisions()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.CreateNodeAsync("projects/a/b");
        Directory.CreateDirectory(repo.FilePath("projects/a_b"));
        await repo.WriteAsync("projects/a_b/index.md", "# Manual legacy node");
        await repo.Get<IIndexService>().RefreshAsync(repo.Root, repo.Token);
        var manifest = JsonNode.Parse(await repo.ReadAsync("indexes/cache/manifest.json"))!;
        var fingerprint = manifest["Fingerprint"]!.GetValue<string>();
        var cached = await repo.Get<INodeListingCacheReader>().TryLoadAsync(repo.Root, fingerprint, repo.Token);
        Assert.NotNull(cached);
        Assert.Contains(cached, node => node.RelativePath == "projects/a/b");
        Assert.Contains(cached, node => node.RelativePath == "projects/a_b");
        Assert.NotEqual(manifest["Nodes"]!["projects/a/b"]!["CacheFile"]!.GetValue<string>(), manifest["Nodes"]!["projects/a_b"]!["CacheFile"]!.GetValue<string>());
        Assert.Contains((await repo.Get<IValidationService>().ValidateAsync(repo.Root, repo.Token)).Issues, issue => issue.RelativePath == "projects/a_b" && issue.Severity == FractalMemory.Core.Domain.Models.ValidationSeverity.Error);
    }

    [Fact]
    public async Task InaccessibleStorageReturnsAValidationIssue()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repo = await RegressionRepository.CreateAsync();
        var directory = repo.FilePath("projects/locked");
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.None);
        try
        {
            var report = await repo.Get<IValidationService>().ValidateAsync(repo.Root, repo.Token);
            Assert.Contains(report.Issues, issue => issue.Message.Contains("permissions", StringComparison.Ordinal));
        }
        finally { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }
    [Fact]
    public async Task CoreReadAndExportResolveConfiguredDefaults()
    {
        using var repo = await RegressionRepository.CreateAsync();
        await repo.ReplaceAsync("config.yaml", "default_depth: 1", "default_depth: 3");
        await repo.ReplaceAsync("config.yaml", "default_export_mode: compact", "default_export_mode: verbose");
        Assert.Equal(RetrievalDepth.Deep, (await repo.Get<IReadService>().OpenAsync(repo.Root, "root", null, NodeViewType.State, repo.Token)).Depth);
        Assert.Equal(ExportMode.Verbose, (await repo.Get<IExportService>().ExportAsync(repo.Root, "root", null, repo.Token)).Mode);
    }

}
