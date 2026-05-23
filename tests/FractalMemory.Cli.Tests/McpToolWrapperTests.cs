using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
using FractalMemory.McpServer;
using FractalMemory.McpServer.Tools;

namespace FractalMemory.Cli.Tests;

public sealed class McpToolWrapperTests
{
    [Fact]
    public async Task MemorySearchToolDelegatesToSearchServiceAndAppliesLimit()
    {
        var expected = new[]
        {
            new SearchResult
            {
                RelativePath = "projects/alpha",
                Title = "Alpha",
                MatchedFile = "index.md",
                SourcePath = "projects/alpha/index.md",
                Snippet = "alpha",
                Score = 100,
            },
            new SearchResult
            {
                RelativePath = "projects/beta",
                Title = "Beta",
                MatchedFile = "state.md",
                SourcePath = "projects/beta/state.md",
                Snippet = "beta",
                Score = 90,
            },
        };

        var context = new StubRepositoryContext("/repo-root");
        var searchService = new StubSearchService(expected);
        var tools = new MemoryTools(
            new StubReadService(),
            searchService,
            new StubExportService(),
            new StubHandoffService(),
            new StubIndexService(),
            new StubValidationService(),
            context);

        var response = await tools.MemorySearch("alpha", 1, cancellationToken: CancellationToken.None);

        Assert.Equal(2, response.Results.Count);
        Assert.Equal("projects/alpha", response.Results[0].RelativePath);
        Assert.Equal("/repo-root", searchService.LastWorkingDirectory);
        Assert.Equal(1, searchService.LastLimit);
    }

    private sealed class StubSearchService(IReadOnlyList<SearchResult> results) : ISearchService
    {
        public string? LastWorkingDirectory { get; private set; }
        public int? LastLimit { get; private set; }

        public string? LastScope { get; private set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string workingDirectory, string query, CancellationToken cancellationToken, int? limit = null, string? scope = null)
        {
            LastLimit = limit;
            LastScope = scope;
            return Task.FromResult(Track(workingDirectory, results));
        }

        public Task<IReadOnlyList<RecentItem>> GetRecentAsync(string workingDirectory, int limit, int days, string? scope, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RecentItem>>([]);

        private IReadOnlyList<SearchResult> Track(string workingDirectory, IReadOnlyList<SearchResult> value)
        {
            LastWorkingDirectory = workingDirectory;
            return value;
        }
    }

    private sealed class StubReadService : IReadService
    {
        public Task<OpenNodeResult> OpenAsync(string workingDirectory, string nodePath, RetrievalDepth depth, NodeViewType view, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubExportService : IExportService
    {
        public Task<ExportDocument> ExportAsync(string workingDirectory, string nodePath, ExportMode mode, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubHandoffService : IHandoffService
    {
        public Task<HandoffPacket> CreateAsync(string workingDirectory, string nodePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubIndexService : IIndexService
    {
        public Task RefreshAsync(string workingDirectory, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubValidationService : IValidationService
    {
        public Task<ValidationReport> ValidateAsync(string workingDirectory, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubRepositoryContext(string workingDirectory) : IMcpRepositoryContext
    {
        public string GetRepositoryRoot() => workingDirectory;
        public string GetServiceWorkingDirectory() => workingDirectory;
        public string GetStoragePath(string relativePath) => throw new NotSupportedException();
        public string GetNodeFilePath(string encodedNodePath, string fileName) => throw new NotSupportedException();
    }
}
