using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

internal sealed class RegressionRepository(ServiceProvider services) : IDisposable
{
    public ServiceProvider Services { get; } = services;
    public string Root { get; } = TestEnvironment.CreateTempDirectory();
    public string Storage => Path.Combine(Root, ".fractal-memory");
    public CancellationToken Token => TestContext.Current.CancellationToken;
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    public string FilePath(string relative) => Path.Combine(Storage, relative.Replace('/', Path.DirectorySeparatorChar));

    public static async Task<RegressionRepository> CreateAsync(ServiceProvider? services = null)
    {
        var repository = new RegressionRepository(services ?? TestEnvironment.CreateServices());
        await repository.Get<IRepositoryService>().InitializeAsync(repository.Root, repository.Token);
        return repository;
    }

    public async Task CreateNodeAsync(string path = "projects/test", NodeFileFormat format = NodeFileFormat.Markdown) =>
        await Get<INodeService>().CreateNodeAsync(Root, path, Token, format);

    public Task WriteAsync(string path, string content) => TestFile.WriteAllTextAsync(FilePath(path), content);
    public Task<string> ReadAsync(string path) => TestFile.ReadAllTextAsync(FilePath(path));

    public async Task ReplaceAsync(string path, string oldValue, string newValue)
    {
        var content = await ReadAsync(path);
        Assert.Contains(oldValue, content, StringComparison.Ordinal);
        await WriteAsync(path, content.Replace(oldValue, newValue, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        Services.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
