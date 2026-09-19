using System.ComponentModel;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.McpServer;
using FractalMemory.McpServer.Tools;
using ModelContextProtocol.Server;

namespace FractalMemory.McpServer.Tools;

[McpServerToolType]
public sealed class MemoryTools(
    IReadService readService,
    ISearchService searchService,
    IExportService exportService,
    IHandoffService handoffService,
    IIndexService indexService,
    IValidationService validationService,
    IMcpRepositoryContext repositoryContext)
{
    [McpServerTool(Name = "memory_open", Title = "Open Memory Node", ReadOnly = true, Idempotent = true)]
    [Description("Open a memory node with layered retrieval depth and optional view selection.")]
    public async Task<FractalMemory.Core.Domain.Models.OpenNodeResult> MemoryOpen(
        [Description("Repository-relative node path, for example projects/fractal-memory-cli.")] string path,
        [Description("Optional retrieval depth. Uses config.yaml default_depth when omitted.")] RetrievalDepth? depth = null,
        [Description("Preferred node view.")] NodeViewType view = NodeViewType.Index,
        CancellationToken cancellationToken = default)
    {
        return await readService.OpenAsync(
            repositoryContext.GetServiceWorkingDirectory(),
            path,
            depth,
            view,
            cancellationToken);
    }

    [McpServerTool(Name = "memory_search", Title = "Search Memory", ReadOnly = true, Idempotent = true)]
    [Description("Search memory nodes by path, title, aliases, tags, and content.")]
    public async Task<SearchResultsResponse> MemorySearch(
        [Description("Search query text.")] string query,
        [Description("Optional maximum number of results. Uses config.yaml diagnostic_top_k when omitted.")] int? limit = null,
        [Description("Optional repository-relative scope prefix such as \"projects/\" or \"research/topic\" that restricts results to matching nodes.")] string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var results = await searchService.SearchAsync(repositoryContext.GetServiceWorkingDirectory(), query, cancellationToken, limit, scope);
        return new SearchResultsResponse(results);
    }

    [McpServerTool(Name = "memory_recent", Title = "Recent Memory Activity", ReadOnly = true, Idempotent = true)]
    [Description("List recent memory activity to help resume work.")]
    public async Task<RecentItemsResponse> MemoryRecent(
        [Description("How many days back to scan.")] int days = 30,
        [Description("Maximum number of items to return.")] int limit = 10,
        [Description("Optional repository-relative scope prefix such as \"projects/\" or \"research/topic\" that restricts results to matching nodes.")] string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var items = await searchService.GetRecentAsync(repositoryContext.GetServiceWorkingDirectory(), limit, days, scope, cancellationToken);
        return new RecentItemsResponse(items);
    }

    [McpServerTool(Name = "memory_export", Title = "Export Memory For AI", ReadOnly = true, Idempotent = true)]
    [Description("Export a memory node into AI-friendly structured text.")]
    public async Task<FractalMemory.Core.Domain.Models.ExportDocument> MemoryExport(
        [Description("Repository-relative node path.")] string path,
        [Description("Optional export mode. Uses config.yaml default_export_mode when omitted.")] ExportMode? mode = null,
        CancellationToken cancellationToken = default)
    {
        return await exportService.ExportAsync(
            repositoryContext.GetServiceWorkingDirectory(),
            path,
            mode,
            cancellationToken);
    }

    [McpServerTool(Name = "memory_handoff_create", Title = "Create Memory Handoff", ReadOnly = false, Idempotent = false)]
    [Description("Create a resumable handoff file for the specified memory node.")]
    public Task<FractalMemory.Core.Domain.Models.HandoffPacket> MemoryHandoffCreate(
        [Description("Repository-relative node path.")] string path,
        CancellationToken cancellationToken = default) =>
        handoffService.CreateAsync(repositoryContext.GetServiceWorkingDirectory(), path, cancellationToken);

    [McpServerTool(Name = "memory_index_refresh", Title = "Refresh Memory Indexes", ReadOnly = false, Idempotent = true)]
    [Description("Rebuild the aliases, tags, and paths indexes from filesystem truth.")]
    public async Task<RefreshIndexesResponse> MemoryIndexRefresh(CancellationToken cancellationToken = default)
    {
        await indexService.RefreshAsync(repositoryContext.GetServiceWorkingDirectory(), cancellationToken);
        return new RefreshIndexesResponse("ok");
    }

    [McpServerTool(Name = "memory_validate", Title = "Validate Memory Repository", ReadOnly = true, Idempotent = true)]
    [Description("Validate repository structure, metadata hygiene, and index freshness.")]
    public Task<FractalMemory.Core.Domain.Models.ValidationReport> MemoryValidate(CancellationToken cancellationToken = default) =>
        validationService.ValidateAsync(repositoryContext.GetServiceWorkingDirectory(), cancellationToken);

}
