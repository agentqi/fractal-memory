using System.ComponentModel;
using System.Text.Json;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Application.UseCases;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FractalMemory.McpServer.Tools;

[McpServerToolType]
public sealed class MemoryWorkflowTools(IMemoryWorkflowService workflow, CreateNodeUseCase createNode, IMcpRepositoryContext context)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string WorkingDirectory => context.GetServiceWorkingDirectory();

    [McpServerTool(Name = "memory_node_create", ReadOnly = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Create a memory node with the repository templates. Existing nodes are never overwritten.")]
    public Task<MemoryNode> Create(string path, NodeFileFormat format = NodeFileFormat.Markdown, CancellationToken cancellationToken = default) =>
        Expected(() => createNode.ExecuteAsync(WorkingDirectory, path, format, cancellationToken));

    [McpServerTool(Name = "memory_read", ReadOnly = true, Idempotent = true)]
    [Description("Read a full contract document or artifacts/ source, optionally by heading. Returns the full-document hash for safe updates and a native resource link.")]
    public async Task<CallToolResult> Read(string path, string file = "state", string? section = null, CancellationToken cancellationToken = default)
    {
        var document = await Expected(() => workflow.ReadAsync(WorkingDirectory, path, file, section, cancellationToken));
        return Linked(document, [document]);
    }

    [McpServerTool(Name = "memory_update", ReadOnly = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Replace an existing Markdown section. Supply expectedHash from memory_read; stale writes are rejected. Unknown sections and metadata are preserved.")]
    public Task<MemoryWriteResult> Update(string path, string section, string content, string expectedHash, string file = "state", CancellationToken cancellationToken = default) =>
        Expected(() => workflow.SetSectionAsync(WorkingDirectory, path, file, section, content, expectedHash, cancellationToken));

    [McpServerTool(Name = "memory_append", ReadOnly = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Append a timeline entry or managed decision. file is timeline or decisions. Supply the latest document hash. A decision may supersede an active decision ID.")]
    public Task<MemoryWriteResult> Append(string path, string file, string content, string expectedHash, string? supersedes = null, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.AppendAsync(WorkingDirectory, path, file, content, expectedHash, supersedes, cancellationToken));

    [McpServerTool(Name = "memory_decisions", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("List managed decision IDs and their current content; superseded decisions are omitted unless requested. Legacy notes remain available through memory_read.")]
    public Task<IReadOnlyList<DecisionEntry>> Decisions(string path, bool includeSuperseded = false, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.DecisionsAsync(WorkingDirectory, path, includeSuperseded, cancellationToken));

    [McpServerTool(Name = "memory_review", ReadOnly = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Record the next review timestamp and optional node status. expectedHash must be from the index document.")]
    public Task<MemoryWriteResult> Review(string path, DateTimeOffset reviewAfter, string expectedHash, NodeStatus? status = null, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.ReviewAsync(WorkingDirectory, path, reviewAfter, status, expectedHash, cancellationToken));

    [McpServerTool(Name = "memory_list", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Discover the memory tree by canonical path, optionally scoped. Archived nodes are excluded by default.")]
    public Task<IReadOnlyList<NodeOverview>> List(string? scope = null, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.ListAsync(WorkingDirectory, scope, includeArchived, cancellationToken));

    [McpServerTool(Name = "memory_attention", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Find overdue review dates, stale timestamps and missing working context. Archived nodes are excluded.")]
    public Task<IReadOnlyList<AttentionItem>> Attention(string? scope = null, int staleDays = 30, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.AttentionAsync(WorkingDirectory, scope, staleDays, cancellationToken));

    [McpServerTool(Name = "memory_context", ReadOnly = true, Idempotent = true)]
    [Description("Build context within a UTF-16 character budget (256 to 1000000). Budget applies to Text; structured metadata and resource links are additional. Optional attachments must be artifacts/ paths.")]
    public async Task<CallToolResult> Context(string path, int maxCharacters = 6000, string[]? artifacts = null, CancellationToken cancellationToken = default)
    {
        var pack = await Expected(() => workflow.ContextAsync(WorkingDirectory, path, maxCharacters, artifacts, cancellationToken));
        return Linked(pack, pack.Sources);
    }

    [McpServerTool(Name = "memory_resume", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Resume a node with bounded context, attention items, its latest handoff and changed contract files. Legacy handoffs explicitly report unavailable comparisons.")]
    public Task<ResumePacket> Resume(string path, int maxCharacters = 6000, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.ResumeAsync(WorkingDirectory, path, maxCharacters, cancellationToken));

    [McpServerTool(Name = "memory_handoff_list", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description("List handoffs belonging to one exact node, latest first.")]
    public Task<IReadOnlyList<HandoffEntry>> Handoffs(string path, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.HandoffsAsync(WorkingDirectory, path, cancellationToken));

    [McpServerTool(Name = "memory_handoff_read", ReadOnly = true, Idempotent = true)]
    [Description("Read the latest handoff for a node, or select a filename returned by memory_handoff_list.")]
    public Task<string> HandoffRead(string path, string? file = null, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.ReadHandoffAsync(WorkingDirectory, path, file, cancellationToken));

    [McpServerTool(Name = "memory_doctor", ReadOnly = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Diagnose repository problems. repair defaults to false; when true it only rebuilds derived indexes after source validation passes.")]
    public Task<DoctorReport> Doctor(bool repair = false, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.DoctorAsync(WorkingDirectory, repair, cancellationToken));

    [McpServerTool(Name = "memory_import", ReadOnly = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Preview importing supplied note content at an explicit node path. sourceName must have .md, .html, .htm or .txt extension. apply defaults to false. Preserves the original source and timestamp; conflicts and duplicate imports block writes. Does not read external host files.")]
    public Task<ImportResult> Import(string path, string sourceName, string content, DateTimeOffset? sourceModified = null, bool apply = false, CancellationToken cancellationToken = default) =>
        Expected(() => workflow.ImportAsync(WorkingDirectory, path, sourceName, content, sourceModified, apply, cancellationToken));

    internal static async Task<T> Expected<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { throw new McpException(exception.Message, exception); }
    }

    private static CallToolResult Linked<T>(T result, IReadOnlyList<MemoryDocument> sources) => new()
    {
        StructuredContent = JsonSerializer.SerializeToElement(result, JsonOptions),
        Content = new ContentBlock[] { new TextContentBlock { Text = JsonSerializer.Serialize(result, JsonOptions) } }
            .Concat(sources.DistinctBy(s => s.ResourceUri).Select(s => (ContentBlock)new ResourceLinkBlock
            {
                Uri = s.ResourceUri,
                Name = s.SourcePath,
                Description = "Full original source document",
                MimeType = s.File.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? "text/markdown" : "text/plain",
            })).ToList(),
    };
}
