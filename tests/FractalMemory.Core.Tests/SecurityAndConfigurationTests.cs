using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Application.UseCases;
using FractalMemory.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

public sealed class SecurityAndConfigurationTests
{
    [Fact]
    public async Task FreshRepositoryAndNodeValidateCleanlyAndUseNodeTitle()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var createNode = provider.GetRequiredService<CreateNodeUseCase>();
        var validationService = provider.GetRequiredService<IValidationService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        var node = await createNode.ExecuteAsync(temp, "projects/demo", NodeFileFormat.Markdown, TestContext.Current.CancellationToken);
        var report = await validationService.ValidateAsync(temp, TestContext.Current.CancellationToken);

        Assert.Equal("Demo", node.Metadata.Title);
        Assert.Equal(new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero), node.Metadata.LastUpdated);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task NodeCreationHonorsDisabledFrontMatter()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var validationService = provider.GetRequiredService<IValidationService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await ReplaceConfigValue(temp, "front_matter: true", "front_matter: false");
        await nodeService.CreateNodeAsync(temp, "projects/plain-markdown", TestContext.Current.CancellationToken);

        var nodeRoot = Path.Combine(temp, ".fractal-memory", "projects", "plain-markdown");
        var index = await TestFile.ReadAllTextAsync(Path.Combine(nodeRoot, "index.md"));
        var report = await validationService.ValidateAsync(temp, TestContext.Current.CancellationToken);

        Assert.StartsWith("# Plain Markdown", index, StringComparison.Ordinal);
        Assert.DoesNotContain("---", index, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Issues, issue =>
            issue.RelativePath == "projects/plain-markdown" &&
            (issue.Message.Contains("title", StringComparison.OrdinalIgnoreCase) ||
             issue.Message.Contains("last_updated", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task RefreshOnWriteCanBeDisabled()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var createNode = provider.GetRequiredService<CreateNodeUseCase>();
        var indexService = provider.GetRequiredService<IIndexService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await ReplaceConfigValue(temp, "refresh_on_write: true", "refresh_on_write: false");
        await createNode.ExecuteAsync(temp, "projects/manual-refresh", NodeFileFormat.Markdown, TestContext.Current.CancellationToken);

        var pathsIndex = Path.Combine(temp, ".fractal-memory", "indexes", "paths.yaml");
        Assert.DoesNotContain("projects/manual-refresh", await TestFile.ReadAllTextAsync(pathsIndex), StringComparison.Ordinal);

        await indexService.RefreshAsync(temp, TestContext.Current.CancellationToken);
        Assert.Contains("projects/manual-refresh", await TestFile.ReadAllTextAsync(pathsIndex), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidHandoffDirectoryIsRejectedBeforeWriting()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        var escapedDirectoryName = $"escaped-{Guid.NewGuid():N}";
        var escapedPath = Path.GetFullPath(Path.Combine(temp, ".fractal-memory", "..", "..", escapedDirectoryName));
        await ReplaceConfigValue(temp, "directory: handoffs", $"directory: ../../{escapedDirectoryName}");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repositoryService.LoadConfigAsync(temp, TestContext.Current.CancellationToken));

        Assert.Contains("relative traversal", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(escapedPath));
    }

    [Fact]
    public async Task HandoffsStayContainedAndRemainUniqueAtTheSameClockTick()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var handoffService = provider.GetRequiredService<IHandoffService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await nodeService.CreateNodeAsync(temp, "projects/handoff", TestContext.Current.CancellationToken);
        var first = await handoffService.CreateAsync(temp, "projects/handoff", TestContext.Current.CancellationToken);
        var second = await handoffService.CreateAsync(temp, "projects/handoff", TestContext.Current.CancellationToken);

        Assert.NotEqual(first.HandoffFilePath, second.HandoffFilePath);
        Assert.StartsWith(".fractal-memory/handoffs/", first.HandoffFilePath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(temp, first.HandoffFilePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(Path.Combine(temp, second.HandoffFilePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task CustomHandoffDirectoryIsExcludedFromContentValidation()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var handoffService = provider.GetRequiredService<IHandoffService>();
        var validationService = provider.GetRequiredService<IValidationService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await ReplaceConfigValue(temp, "directory: handoffs", "directory: exports/handoffs");
        await nodeService.CreateNodeAsync(temp, "projects/custom-handoff", TestContext.Current.CancellationToken);
        var handoff = await handoffService.CreateAsync(temp, "projects/custom-handoff", TestContext.Current.CancellationToken);
        var report = await validationService.ValidateAsync(temp, TestContext.Current.CancellationToken);

        Assert.StartsWith(".fractal-memory/exports/handoffs/", handoff.HandoffFilePath, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Issues, issue =>
            issue.RelativePath.StartsWith("exports/handoffs/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SymlinkedNodeContentIsRejectedAndReported()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var validationService = provider.GetRequiredService<IValidationService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await nodeService.CreateNodeAsync(temp, "projects/linked", TestContext.Current.CancellationToken);
        var indexPath = Path.Combine(temp, ".fractal-memory", "projects", "linked", "index.md");
        var outsidePath = Path.Combine(temp, "outside.md");
        await TestFile.WriteAllTextAsync(outsidePath, "# Outside\n\nSecret marker.");
        File.Delete(indexPath);
        File.CreateSymbolicLink(indexPath, outsidePath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nodeService.GetNodeAsync(temp, "projects/linked", TestContext.Current.CancellationToken));
        var report = await validationService.ValidateAsync(temp, TestContext.Current.CancellationToken);

        Assert.Contains("missing index", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.Issues, issue =>
            issue.RelativePath == "projects/linked/index.md" &&
            issue.Message.Contains("Symbolic links", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SymlinkedSearchIndexCannotReadOutsideStorage()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var searchService = provider.GetRequiredService<ISearchService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        var outsidePath = Path.Combine(temp, "outside.yaml");
        await TestFile.WriteAllTextAsync(outsidePath, "outside: secret");
        var pathsIndex = Path.Combine(temp, ".fractal-memory", "indexes", "paths.yaml");
        File.Delete(pathsIndex);
        File.CreateSymbolicLink(pathsIndex, outsidePath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            searchService.GetRecentAsync(temp, 10, 30, null, TestContext.Current.CancellationToken));

        Assert.Contains("Symbolic links", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PoisonedCacheManifestCannotDeleteOutsideFiles()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var indexService = provider.GetRequiredService<IIndexService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        var outsidePath = Path.Combine(temp, "outside.txt");
        await TestFile.WriteAllTextAsync(outsidePath, "keep me");
        var cacheRoot = Path.Combine(temp, ".fractal-memory", "indexes", "cache");
        Directory.CreateDirectory(cacheRoot);
        await TestFile.WriteAllTextAsync(
            Path.Combine(cacheRoot, "manifest.json"),
            """
            {
              "RefreshedAt": "2026-04-02T12:00:00+00:00",
              "Fingerprint": "poisoned",
              "Nodes": {
                "projects/poisoned": {
                  "CacheFile": "../../../outside.txt",
                  "CachedAt": "2026-04-02T12:00:00+00:00",
                  "FileHashes": {}
                }
              }
            }
            """);

        await indexService.RefreshAsync(temp, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(outsidePath));
        Assert.Equal("keep me", await TestFile.ReadAllTextAsync(outsidePath));
    }

    [Fact]
    public async Task InvalidRetrievalLimitsAreRejected()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
        await ReplaceConfigValue(temp, "diagnostic_top_k: 10", "diagnostic_top_k: 0");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repositoryService.LoadConfigAsync(temp, TestContext.Current.CancellationToken));
        Assert.Contains("positive integers", exception.Message, StringComparison.Ordinal);
    }

    private static async Task ReplaceConfigValue(string repositoryRoot, string oldValue, string newValue)
    {
        var configPath = Path.Combine(repositoryRoot, ".fractal-memory", "config.yaml");
        var config = await TestFile.ReadAllTextAsync(configPath);
        Assert.Contains(oldValue, config, StringComparison.Ordinal);
        await TestFile.WriteAllTextAsync(configPath, config.Replace(oldValue, newValue, StringComparison.Ordinal));
    }
}
