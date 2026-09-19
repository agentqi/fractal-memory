using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Application.Services;

public interface IMemoryWorkflowService
{
    Task<MemoryDocument> ReadAsync(string wd, string path, string file, string? section, CancellationToken ct);
    Task<MemoryWriteResult> SetSectionAsync(string wd, string path, string file, string section, string content, string expectedHash, CancellationToken ct);
    Task<MemoryWriteResult> AppendAsync(string wd, string path, string file, string content, string expectedHash, string? supersedes, CancellationToken ct);
    Task<IReadOnlyList<DecisionEntry>> DecisionsAsync(string wd, string path, bool includeSuperseded, CancellationToken ct);
    Task<MemoryWriteResult> ReviewAsync(string wd, string path, DateTimeOffset reviewAfter, NodeStatus? status, string expectedHash, CancellationToken ct);
    Task<IReadOnlyList<NodeOverview>> ListAsync(string wd, string? scope, bool includeArchived, CancellationToken ct);
    Task<IReadOnlyList<AttentionItem>> AttentionAsync(string wd, string? scope, int staleDays, CancellationToken ct);
    Task<ContextPack> ContextAsync(string wd, string path, int maxCharacters, IReadOnlyList<string>? artifacts, CancellationToken ct);
    Task<IReadOnlyList<HandoffEntry>> HandoffsAsync(string wd, string path, CancellationToken ct);
    Task<string> ReadHandoffAsync(string wd, string path, string? file, CancellationToken ct);
    Task<ResumePacket> ResumeAsync(string wd, string path, int maxCharacters, CancellationToken ct);
    Task<DoctorReport> DoctorAsync(string wd, bool repair, CancellationToken ct);
    Task<ImportResult> ImportAsync(string wd, string path, string sourceName, string content, DateTimeOffset? sourceModified, bool apply, CancellationToken ct);
}
