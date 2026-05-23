using FractalMemory.Core.Application;
using FractalMemory.Core.Application.Services;
using FractalMemory.McpServer;
using FractalMemory.McpServer.Resources;
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
            await repositoryService.InitializeAsync(temp, CancellationToken.None);
            await nodeService.CreateNodeAsync(temp, "projects/alpha", CancellationToken.None);
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
            await repositoryService.InitializeAsync(temp, CancellationToken.None);
            Environment.SetEnvironmentVariable("FRACTALMEM_REPOSITORY_ROOT", temp);

            var resources = new MemoryResources(fileSystemService, new McpRepositoryContext(repositoryService));

            await Assert.ThrowsAsync<InvalidOperationException>(() => resources.NodeIndex("..%2F..%2Fetc", CancellationToken.None));
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
}
