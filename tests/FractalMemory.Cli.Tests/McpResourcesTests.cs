using FractalMemory.Core.Application;
using FractalMemory.Core.Application.Services;
using FractalMemory.McpServer;
using FractalMemory.McpServer.Resources;
using FractalMemory.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Cli.Tests;

public sealed class McpResourcesTests
{
    [Fact]
    public async Task ResourceContextUsesConfiguredRepositoryRoot()
    {
        using var provider = CreateProvider();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var temp = CreateTempDirectory();
        var original = Environment.GetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT");

        try
        {
            await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
            await nodeService.CreateNodeAsync(temp, "projects/alpha", TestContext.Current.CancellationToken);
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", temp);

            var context = new McpRepositoryContext(repositoryService);

            Assert.Equal(temp, context.GetRepositoryRoot());
            Assert.Contains(Path.Combine(".fractal-memory", "projects", "alpha"), context.GetNodeFilePath("projects%2Falpha", "index.md"), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", original);
        }
    }

    [Fact]
    public async Task NodeResourcesRejectTraversal()
    {
        using var provider = CreateProvider();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var fileSystemService = provider.GetRequiredService<IFileSystemService>();
        var temp = CreateTempDirectory();
        var original = Environment.GetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT");

        try
        {
            await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", temp);

            var resources = new MemoryResources(fileSystemService, repositoryService, new McpRepositoryContext(repositoryService));

            await Assert.ThrowsAsync<InvalidOperationException>(() => resources.NodeIndex("..%2F..%2Fetc", TestContext.Current.CancellationToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", original);
        }
    }

    [Fact]
    public async Task ResourcesHonorHtmlRootAndConfiguredHandoffDirectory()
    {
        using var provider = CreateProvider();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var handoffService = provider.GetRequiredService<IHandoffService>();
        var fileSystemService = provider.GetRequiredService<IFileSystemService>();
        var temp = CreateTempDirectory();
        var original = Environment.GetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT");

        try
        {
            await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
            await ReplaceConfigValue(temp, "directory: handoffs", "directory: handoffs/custom");
            await nodeService.CreateNodeAsync(temp, "projects/resources", TestContext.Current.CancellationToken);
            await handoffService.CreateAsync(temp, "projects/resources", TestContext.Current.CancellationToken);

            var rootDirectory = Path.Combine(temp, ".fractal-memory", "root");
            File.Delete(Path.Combine(rootDirectory, "index.md"));
            await TestFile.WriteAllTextAsync(
                Path.Combine(rootDirectory, "index.html"),
                "<html><body><h1>HTML Root</h1></body></html>");
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", temp);

            var resources = new MemoryResources(fileSystemService, repositoryService, new McpRepositoryContext(repositoryService));
            var root = await resources.RootIndex(TestContext.Current.CancellationToken);
            var handoff = await resources.LatestHandoff(TestContext.Current.CancellationToken);

            Assert.Contains("HTML Root", root, StringComparison.Ordinal);
            Assert.Contains("Handoff: Resources", handoff, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", original);
        }
    }

    [Fact]
    public async Task ResourceRejectsSymlinkedNodeFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var provider = CreateProvider();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var fileSystemService = provider.GetRequiredService<IFileSystemService>();
        var temp = CreateTempDirectory();
        var original = Environment.GetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT");

        try
        {
            await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
            await nodeService.CreateNodeAsync(temp, "projects/linked", TestContext.Current.CancellationToken);
            var outside = Path.Combine(temp, "outside.md");
            await TestFile.WriteAllTextAsync(outside, "outside marker");
            var index = Path.Combine(temp, ".fractal-memory", "projects", "linked", "index.md");
            File.Delete(index);
            File.CreateSymbolicLink(index, outside);
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", temp);

            var resources = new MemoryResources(fileSystemService, repositoryService, new McpRepositoryContext(repositoryService));
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                resources.NodeIndex("projects%2Flinked", TestContext.Current.CancellationToken));

            Assert.Contains("Symbolic links", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", original);
        }
    }

    [Fact]
    public async Task McpToolsHonorConfiguredDefaults()
    {
        using var provider = CreateProvider();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var nodeService = provider.GetRequiredService<INodeService>();
        var temp = CreateTempDirectory();
        var original = Environment.GetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT");

        try
        {
            await repositoryService.InitializeAsync(temp, TestContext.Current.CancellationToken);
            await nodeService.CreateNodeAsync(temp, "projects/defaults", TestContext.Current.CancellationToken);
            await ReplaceConfigValue(temp, "default_depth: 1", "default_depth: 3");
            await ReplaceConfigValue(temp, "default_export_mode: compact", "default_export_mode: verbose");
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", temp);
            var context = new McpRepositoryContext(repositoryService);
            var tools = new MemoryTools(
                provider.GetRequiredService<IReadService>(),
                provider.GetRequiredService<ISearchService>(),
                provider.GetRequiredService<IExportService>(),
                provider.GetRequiredService<IHandoffService>(),
                provider.GetRequiredService<IIndexService>(),
                provider.GetRequiredService<IValidationService>(),
                repositoryService,
                context);

            var opened = await tools.MemoryOpen("projects/defaults", cancellationToken: TestContext.Current.CancellationToken);
            var exported = await tools.MemoryExport("projects/defaults", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(FractalMemory.Core.Domain.Enums.RetrievalDepth.Deep, opened.Depth);
            Assert.Equal(FractalMemory.Core.Domain.Enums.ExportMode.Verbose, exported.Mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", original);
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddFractalMemoryCore();
        return services.BuildServiceProvider();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "fractalmem-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task ReplaceConfigValue(string repositoryRoot, string oldValue, string newValue)
    {
        var configPath = Path.Combine(repositoryRoot, ".fractal-memory", "config.yaml");
        var config = await TestFile.ReadAllTextAsync(configPath);
        Assert.Contains(oldValue, config, StringComparison.Ordinal);
        await TestFile.WriteAllTextAsync(configPath, config.Replace(oldValue, newValue, StringComparison.Ordinal));
    }
}
