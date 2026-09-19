using FractalMemory.Core.Domain.Enums;

namespace FractalMemory.Core.Domain.Models;

public sealed record MemoryDocument(string NodePath, string File, string SourcePath, string ResourceUri,
    string Hash, string Content, string? Section, int? StartLine, int? EndLine);
public sealed record MemoryWriteResult(MemoryDocument Document, IReadOnlyList<string> Warnings, string? DecisionId = null);
public sealed record DecisionEntry(string Id, string Status, string RecordedAt, string? Supersedes, string Content);
public sealed record NodeOverview(string Path, string Title, NodeStatus Status, DateTimeOffset? LastUpdated, DateTimeOffset? ReviewAfter);
public sealed record AttentionItem(string Path, IReadOnlyList<string> Reasons);
public sealed record ContextPack(string NodePath, string Text, int MaxCharacters, int UsedCharacters, bool Truncated,
    IReadOnlyList<string> OmittedSections, IReadOnlyList<MemoryDocument> Sources, IReadOnlyList<string> Warnings);
public sealed record HandoffEntry(string File, DateTimeOffset? CreatedAt, bool ComparisonAvailable);
public sealed record ResumePacket(ContextPack Context, HandoffEntry? LatestHandoff, IReadOnlyList<string> ChangedFiles,
    bool ComparisonAvailable, IReadOnlyList<AttentionItem> Attention);
public sealed record DoctorReport(ValidationReport Validation, bool IndexesRebuilt, IReadOnlyList<string> Advice);
public sealed record ImportResult(string NodePath, string SourceName, string SourceHash, bool CanApply, bool Applied,
    IReadOnlyList<string> Conflicts, IReadOnlyList<string> DuplicateNodes, IReadOnlyList<string> Warnings);
