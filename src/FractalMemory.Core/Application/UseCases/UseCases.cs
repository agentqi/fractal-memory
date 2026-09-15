using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Application.UseCases;

public sealed class InitRepositoryUseCase(IRepositoryService repositoryService)
{
    public Task<string> ExecuteAsync(string workingDirectory, CancellationToken cancellationToken) =>
        repositoryService.InitializeAsync(workingDirectory, cancellationToken);
}

public sealed class CreateNodeUseCase(
    INodeService nodeService,
    IRepositoryService repositoryService,
    IIndexService indexService)
{
    public async Task<MemoryNode> ExecuteAsync(
        string workingDirectory,
        string nodePath,
        NodeFileFormat format,
        CancellationToken cancellationToken)
    {
        var node = await nodeService.CreateNodeAsync(workingDirectory, nodePath, cancellationToken, format);
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        if (config.Indexing.Enabled && config.Indexing.RefreshOnWrite)
        {
            await indexService.RefreshAsync(repositoryRoot, cancellationToken);
        }

        return node;
    }
}

public sealed class OpenNodeUseCase(IReadService readService)
{
    public Task<OpenNodeResult> ExecuteAsync(
        string workingDirectory,
        string nodePath,
        RetrievalDepth depth,
        NodeViewType view,
        CancellationToken cancellationToken) =>
        readService.OpenAsync(workingDirectory, nodePath, depth, view, cancellationToken);
}

public sealed class SearchUseCase(ISearchService searchService)
{
    public Task<IReadOnlyList<SearchResult>> ExecuteAsync(
        string workingDirectory,
        string query,
        CancellationToken cancellationToken,
        int? limit = null,
        string? scope = null) =>
        searchService.SearchAsync(workingDirectory, query, cancellationToken, limit, scope);
}

public sealed class ExportUseCase(IExportService exportService)
{
    public Task<ExportDocument> ExecuteAsync(
        string workingDirectory,
        string nodePath,
        ExportMode mode,
        CancellationToken cancellationToken) =>
        exportService.ExportAsync(workingDirectory, nodePath, mode, cancellationToken);
}

public sealed class CreateHandoffUseCase(IHandoffService handoffService)
{
    public Task<HandoffPacket> ExecuteAsync(string workingDirectory, string nodePath, CancellationToken cancellationToken) =>
        handoffService.CreateAsync(workingDirectory, nodePath, cancellationToken);
}

public sealed class GetRecentUseCase(ISearchService searchService)
{
    public Task<IReadOnlyList<RecentItem>> ExecuteAsync(
        string workingDirectory,
        int limit,
        int days,
        string? scope,
        CancellationToken cancellationToken) =>
        searchService.GetRecentAsync(workingDirectory, limit, days, scope, cancellationToken);
}

public sealed class RefreshIndexesUseCase(IIndexService indexService)
{
    public Task ExecuteAsync(string workingDirectory, CancellationToken cancellationToken) =>
        indexService.RefreshAsync(workingDirectory, cancellationToken);
}

public sealed class ValidateRepositoryUseCase(IValidationService validationService)
{
    public Task<ValidationReport> ExecuteAsync(string workingDirectory, CancellationToken cancellationToken) =>
        validationService.ValidateAsync(workingDirectory, cancellationToken);
}
