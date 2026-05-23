using FractalMemory.Core.Domain.Models;

namespace FractalMemory.McpServer.Tools;

public sealed record SearchResultsResponse(IReadOnlyList<SearchResult> Results);

public sealed record RecentItemsResponse(IReadOnlyList<RecentItem> Items);

public sealed record RefreshIndexesResponse(string Status);
