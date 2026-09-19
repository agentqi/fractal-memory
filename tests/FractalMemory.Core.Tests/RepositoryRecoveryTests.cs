using FractalMemory.Core.Application;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

public sealed class RepositoryRecoveryTests
{
    [Fact]
    public async Task InvalidConfigurationDoesNotLeaveANodeAndCanBeRetried()
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.ReplaceAsync("config.yaml", "diagnostic_top_k: 10", "diagnostic_top_k: 0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateNodeAsync());
        Assert.False(Directory.Exists(repository.FilePath("projects/test")));
        await repository.ReplaceAsync("config.yaml", "diagnostic_top_k: 0", "diagnostic_top_k: 10");
        await repository.CreateNodeAsync();
        Assert.True(File.Exists(repository.FilePath("projects/test/state.md")));
    }

    [Fact]
    public async Task MalformedTemplateDoesNotPublishAPartialNode()
    {
        using var repository = await RegressionRepository.CreateAsync();
        var template = await repository.ReadAsync("templates/node/state.md");
        await repository.WriteAsync("templates/node/state.md", "---\ntitle: [broken\n---\nState");
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateNodeAsync());
        Assert.False(Directory.Exists(repository.FilePath("projects/test")));
        Assert.Empty(Directory.EnumerateDirectories(repository.FilePath(".staging")));
        await repository.WriteAsync("templates/node/state.md", template);
        await repository.CreateNodeAsync();
        Assert.True(File.Exists(repository.FilePath("projects/test/state.md")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedWritesCleanUpOnlyTheirOwnStageAndAllowRetry(bool cancel)
    {
        var services = new ServiceCollection().AddFractalMemoryCore();
        var files = new InterruptedFileSystem(cancel);
        services.AddSingleton<IFileSystemService>(files);
        using var repository = await RegressionRepository.CreateAsync(services.BuildServiceProvider());
        files.Interrupt = true;
        var exception = await Record.ExceptionAsync(() => repository.CreateNodeAsync());
        Assert.NotNull(exception);
        Assert.True(cancel ? exception is OperationCanceledException : exception is IOException);
        Assert.False(Directory.Exists(repository.FilePath("projects/test")));
        Assert.Empty(Directory.EnumerateDirectories(repository.FilePath(".staging")));
        files.Interrupt = false;
        await repository.CreateNodeAsync();
        Assert.True(File.Exists(repository.FilePath("projects/test/decisions.md")));
    }

    [Fact]
    public async Task ValidationDetectsLegacyIncompleteScaffoldsButAllowsOrganizationalFolders()
    {
        using var repository = await RegressionRepository.CreateAsync();
        Directory.CreateDirectory(repository.FilePath("projects/incomplete/children"));
        Directory.CreateDirectory(repository.FilePath("projects/incomplete/artifacts"));
        Directory.CreateDirectory(repository.FilePath("projects/organization/team"));
        Directory.CreateDirectory(repository.FilePath("projects/timeline-only"));
        await repository.WriteAsync("projects/timeline-only/timeline.md", "# Timeline\n\nStarted work.");
        var report = await repository.Get<IValidationService>().ValidateAsync(repository.Root, repository.Token);
        foreach (var node in new[] { "projects/incomplete", "projects/timeline-only" })
        {
            Assert.Contains(report.Issues, issue => issue.RelativePath == node && issue.Severity == ValidationSeverity.Error && issue.Message.Contains("Missing required index", StringComparison.Ordinal));
            Assert.Contains(report.Issues, issue => issue.RelativePath == node && issue.Severity == ValidationSeverity.Error && issue.Message.Contains("Missing required state", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(report.Issues, issue => issue.RelativePath.StartsWith("projects/organization", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentCreationPublishesExactlyOneCompleteFormat()
    {
        using var repository = await RegressionRepository.CreateAsync();
        var errors = await Task.WhenAll(new[] { NodeFileFormat.Markdown, NodeFileFormat.Html }.Select(format =>
            Record.ExceptionAsync(() => repository.CreateNodeAsync(format: format)).AsTask()));
        Assert.Single(errors, error => error is null);
        Assert.Single(errors, error => error is IOException or InvalidOperationException);
        var files = Directory.GetFiles(repository.FilePath("projects/test"));
        Assert.Equal(4, files.Length);
        Assert.Single(files.Select(Path.GetExtension).Distinct());
        Assert.Empty(Directory.EnumerateDirectories(repository.FilePath(".staging")));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("-1")]
    public async Task UndefinedExportModeInConfigurationIsRejected(string value)
    {
        using var repository = await RegressionRepository.CreateAsync();
        await repository.ReplaceAsync("config.yaml", "default_export_mode: compact", $"default_export_mode: {value}");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Get<IRepositoryService>()
            .LoadConfigAsync(repository.Root, repository.Token));
        Assert.Contains("default_export_mode", error.Message, StringComparison.Ordinal);
    }

    private sealed class InterruptedFileSystem(bool cancel) : IFileSystemService
    {
        private readonly LocalFileSystemService inner = new();
        public bool Interrupt { get; set; }
        public Task WriteAllTextAsync(string path, string content, CancellationToken token)
        {
            if (Interrupt && path.Contains($"{Path.DirectorySeparatorChar}.staging{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && Path.GetFileName(path) == "state.md")
            {
                if (cancel) throw new OperationCanceledException("Injected cancellation.");
                throw new IOException("Injected disk failure.");
            }
            return inner.WriteAllTextAsync(path, content, token);
        }
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public bool FileExists(string path) => inner.FileExists(path);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void MoveDirectory(string source, string destination) => inner.MoveDirectory(source, destination);
        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);
        public Task<string> ReadAllTextAsync(string path, CancellationToken token) => inner.ReadAllTextAsync(path, token);
        public IEnumerable<string> EnumerateDirectories(string path) => inner.EnumerateDirectories(path);
        public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option) => inner.EnumerateFiles(path, pattern, option);
        public DateTimeOffset GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(path);
        public void DeleteFile(string path) => inner.DeleteFile(path);
    }
}
