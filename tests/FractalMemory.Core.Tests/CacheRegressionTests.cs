using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

public sealed class CacheRegressionTests
{
    [Fact]
    public async Task OlderCacheFormatIsIgnoredAndUpgradedOnRefresh()
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync();
        await repository.Get<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        const string cachePath = "indexes/cache/nodes/projects_test.json";
        var document = JsonNode.Parse(await repository.ReadAsync(cachePath))!;
        document["Metadata"]!["Title"] = "OldFormatTitle";
        var altered = document.ToJsonString();
        await repository.WriteAsync(cachePath, altered);
        var manifest = JsonNode.Parse(await repository.ReadAsync("indexes/cache/manifest.json"))!;
        manifest.AsObject().Remove("FormatVersion");
        manifest["Nodes"]!["projects/test"]!["CacheHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(altered)));
        await repository.WriteAsync("indexes/cache/manifest.json", manifest.ToJsonString());
        using var cold = TestEnvironment.CreateServices();
        Assert.Equal("Test", Assert.Single(await cold.GetRequiredService<ISearchService>()
            .SearchAsync(repository.Root, "projects/test", repository.Token)).Title);
        await cold.GetRequiredService<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        Assert.Equal(2, JsonNode.Parse(await repository.ReadAsync("indexes/cache/manifest.json"))!["FormatVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task MetadataOnlyChangesSurviveRefreshInWarmAndColdServices()
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync();
        var indexes = repository.Get<IIndexService>();
        await indexes.RefreshAsync(repository.Root, repository.Token);
        await repository.Get<ISearchService>().SearchAsync(repository.Root, "projects/test", repository.Token);

        await repository.ReplaceAsync("projects/test/index.md", "title: Test", "title: Renamed\naliases: [newalias]\ntags: [newtag]\nowner: NewOwner");
        await repository.ReplaceAsync("projects/test/index.md", "status: active", "status: paused");
        await repository.ReplaceAsync("projects/test/index.md", "priority: medium", "priority: critical");
        await repository.ReplaceAsync("projects/test/index.md", "summary: Summary for Test.", "summary: Updated summary.");
        await indexes.RefreshAsync(repository.Root, repository.Token);

        using var cold = TestEnvironment.CreateServices();
        foreach (var provider in new[] { repository.Services, cold })
        {
            var nodes = await provider.GetRequiredService<INodeService>().GetAllNodesAsync(repository.Root, repository.Token);
            var node = Assert.Single(nodes, node => node.RelativePath == "projects/test");
            Assert.Equal("Renamed", node.Metadata.Title);
            Assert.Equal("NewOwner", node.Metadata.Owner);
            Assert.Equal("Updated summary.", node.Metadata.Summary);
            Assert.Equal(NodeStatus.Paused, node.Metadata.Status);
            Assert.Equal(PriorityLevel.Critical, node.Metadata.Priority);
            foreach (var query in new[] { "Renamed", "newalias", "newtag" })
            {
                var hits = await provider.GetRequiredService<ISearchService>().SearchAsync(repository.Root, query, repository.Token);
                Assert.Contains(hits, hit => hit.RelativePath == "projects/test" && hit.Title == "Renamed");
            }
        }
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Metadata\":null}")]
    public async Task RefreshRepairsDamagedCacheFiles(string damaged)
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync();
        await repository.Get<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        const string cachePath = "indexes/cache/nodes/projects_test.json";
        var expected = await repository.ReadAsync(cachePath);
        await repository.WriteAsync(cachePath, damaged);

        using var cold = TestEnvironment.CreateServices();
        var results = await cold.GetRequiredService<ISearchService>().SearchAsync(repository.Root, "projects/test", repository.Token);
        Assert.Equal("Test", Assert.Single(results).Title);
        await cold.GetRequiredService<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        Assert.Equal(expected, await repository.ReadAsync(cachePath));
    }

    [Fact]
    public async Task ExplicitRefreshBypassesEvenInternallyConsistentWarmCache()
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync();
        await repository.Get<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        const string cachePath = "indexes/cache/nodes/projects_test.json";
        var document = JsonNode.Parse(await repository.ReadAsync(cachePath))!;
        document["Metadata"]!["Title"] = "CacheOnlyFakeTitle";
        var altered = document.ToJsonString();
        await repository.WriteAsync(cachePath, altered);
        var manifest = JsonNode.Parse(await repository.ReadAsync("indexes/cache/manifest.json"))!;
        manifest["Nodes"]!["projects/test"]!["CacheHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(altered)));
        await repository.WriteAsync("indexes/cache/manifest.json", manifest.ToJsonString());

        using var cold = TestEnvironment.CreateServices();
        var search = cold.GetRequiredService<ISearchService>();
        Assert.Equal("CacheOnlyFakeTitle", Assert.Single(await search.SearchAsync(repository.Root, "projects/test", repository.Token)).Title);
        await cold.GetRequiredService<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        Assert.Equal("Test", Assert.Single(await search.SearchAsync(repository.Root, "projects/test", repository.Token)).Title);
        Assert.DoesNotContain("CacheOnlyFakeTitle", await repository.ReadAsync("indexes/paths.yaml"), StringComparison.Ordinal);
        Assert.DoesNotContain("CacheOnlyFakeTitle", await repository.ReadAsync(cachePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitRefreshDetectsSourceEditsWithPreservedModificationTime()
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync();
        await repository.Get<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        var path = repository.FilePath("projects/test/index.md");
        var originalTime = File.GetLastWriteTimeUtc(path);
        await repository.ReplaceAsync("projects/test/index.md", "title: Test", "title: Fresh");
        File.SetLastWriteTimeUtc(path, originalTime);
        await repository.Get<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        using var cold = TestEnvironment.CreateServices();
        Assert.Equal("Fresh", Assert.Single(await cold.GetRequiredService<ISearchService>()
            .SearchAsync(repository.Root, "projects/test", repository.Token)).Title);
    }

    [Theory]
    [InlineData("{\"Nodes\":null}")]
    [InlineData("{broken")]
    public async Task RefreshRecoversMalformedManifest(string damaged)
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.CreateNodeAsync();
        await repository.Get<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        await repository.WriteAsync("indexes/cache/manifest.json", damaged);
        using var cold = TestEnvironment.CreateServices();
        await cold.GetRequiredService<IIndexService>().RefreshAsync(repository.Root, repository.Token);
        Assert.NotNull(JsonNode.Parse(await repository.ReadAsync("indexes/cache/manifest.json"))!["Nodes"]!["projects/test"]);
    }
}
