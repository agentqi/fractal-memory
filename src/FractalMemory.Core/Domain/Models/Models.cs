using FractalMemory.Core.Domain.Enums;

namespace FractalMemory.Core.Domain.Models;

public sealed class RepositoryConfig
{
    public string Version { get; init; } = "0.1";
    public RetrievalDepth DefaultDepth { get; init; } = RetrievalDepth.Orientation;
    public ExportMode DefaultExportMode { get; init; } = ExportMode.Compact;
    public IndexingOptions Indexing { get; init; } = new();
    public RetrievalOptions Retrieval { get; init; } = new();
    public MetadataOptions Metadata { get; init; } = new();
    public HandoffOptions Handoffs { get; init; } = new();
    public ValidationOptions Validation { get; init; } = new();
}

public sealed class IndexingOptions
{
    public bool Enabled { get; init; } = true;
    public bool RefreshOnWrite { get; init; }
}

public sealed class RetrievalOptions
{
    public int DiagnosticTopK { get; init; } = 10;
    public int AnswerTopK { get; init; } = 3;
    public int MaxSnippetLines { get; init; } = 4;
}

public sealed class MetadataOptions
{
    public bool FrontMatter { get; init; } = true;
}

public sealed class HandoffOptions
{
    public string Directory { get; init; } = "handoffs";
}

public sealed class ValidationOptions
{
    public bool RequireIndex { get; init; } = true;
    public bool RequireState { get; init; } = true;
}

public sealed class MemoryNode
{
    public required string RelativePath { get; init; }
    public required string FullPath { get; init; }
    public required NodeMetadata Metadata { get; init; }
    public string IndexFileName { get; init; } = "index.md";
    public string StateFileName { get; init; } = "state.md";
    public string TimelineFileName { get; init; } = "timeline.md";
    public string DecisionsFileName { get; init; } = "decisions.md";
    public string IndexContent { get; init; } = string.Empty;
    public string StateContent { get; init; } = string.Empty;
    public string TimelineContent { get; init; } = string.Empty;
    public string DecisionsContent { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> SourceHashes { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, int> SourceStartLines { get; init; } = new Dictionary<string, int>();
}

public sealed class NodeMetadata
{
    public string? Title { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public NodeStatus Status { get; init; } = NodeStatus.Active;
    public PriorityLevel Priority { get; init; } = PriorityLevel.Medium;
    public DateTimeOffset? LastUpdated { get; init; }
    public string? Owner { get; init; }
    public string? Summary { get; init; }
}

public sealed class SearchResult
{
    public required string RelativePath { get; init; }
    public required string Title { get; init; }
    public required string MatchedFile { get; init; }
    public required string SourcePath { get; init; }
    public required string Snippet { get; init; }
    public string? SectionHeading { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public required int Score { get; init; }
    public IReadOnlyDictionary<string, int> ScoreBreakdown { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
}

public sealed class HandoffPacket
{
    public required string RelativePath { get; init; }
    public required string HandoffFilePath { get; init; }
    public required string ProjectBranch { get; init; }
    public required string CurrentGoal { get; init; }
    public required string CurrentState { get; init; }
    public IReadOnlyList<string> RecentDecisions { get; init; } = [];
    public IReadOnlyList<string> ActiveConstraints { get; init; } = [];
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];
    public IReadOnlyList<string> NextBestActions { get; init; } = [];
    public IReadOnlyList<string> ReadFirstFiles { get; init; } = [];
    public IReadOnlyList<string> SourceReferences { get; init; } = [];
    public string RenderedContent { get; init; } = string.Empty;
}

public sealed class OpenNodeResult
{
    public required string RelativePath { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public RetrievalDepth Depth { get; init; }
    public NodeViewType View { get; init; }
    public string? IndexSummary { get; init; }
    public string? CurrentState { get; init; }
    public bool StateTruncated { get; init; }
    public IReadOnlyList<string> Children { get; init; } = [];
    public IReadOnlyList<string> SuggestedReads { get; init; } = [];
    public IReadOnlyList<string> RecentTimeline { get; init; } = [];
    public IReadOnlyList<string> RecentDecisions { get; init; } = [];
}

public sealed class RecentItem
{
    public required string RelativePath { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public required DateTimeOffset LastModified { get; init; }
}

public sealed class ValidationReport
{
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = [];
    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);
}

public sealed class ValidationIssue
{
    public required ValidationSeverity Severity { get; init; }
    public required string RelativePath { get; init; }
    public required string Message { get; init; }
}

public enum ValidationSeverity
{
    Warning,
    Error,
}

public sealed class ParsedMarkdownDocument
{
    public required NodeMetadata Metadata { get; init; }
    public required string Content { get; init; }
    public bool HasFrontMatter { get; init; }
    public int ContentStartLine { get; init; } = 1;
    public string SourceHash { get; init; } = string.Empty;
}

public sealed class ExportDocument
{
    public required string RelativePath { get; init; }
    public required string Title { get; init; }
    public ExportMode Mode { get; init; }
    public required string Summary { get; init; }
    public required string CurrentState { get; init; }
    public IReadOnlyList<string> Children { get; init; } = [];
    public IReadOnlyList<string> TimelineHighlights { get; init; } = [];
    public IReadOnlyList<string> DecisionHighlights { get; init; } = [];
    public AnswerContextPacket? AnswerContext { get; init; }
}

public sealed class AnswerContextPacket
{
    public required string ProjectBranch { get; init; }
    public string? CurrentObjective { get; init; }
    public IReadOnlyList<string> KeyPriorDecisions { get; init; } = [];
    public IReadOnlyList<string> ActiveConstraints { get; init; } = [];
    public IReadOnlyList<string> NextBestActions { get; init; } = [];
    public IReadOnlyList<string> MissingInformation { get; init; } = [];
    public IReadOnlyList<SupportingSource> SupportingSources { get; init; } = [];
    public IReadOnlyList<SearchResult> DiagnosticResults { get; init; } = [];
}

public sealed class SupportingSource
{
    public required string SourcePath { get; init; }
    public string? SectionHeading { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public required string Excerpt { get; init; }
}

public sealed class StructuredMemoryFields
{
    public string? ProjectBranch { get; init; }
    public string? CurrentObjective { get; init; }
    public IReadOnlyList<string> KeyDecisionsInForce { get; init; } = [];
    public IReadOnlyList<string> ActiveConstraints { get; init; } = [];
    public IReadOnlyList<string> NextBestActions { get; init; } = [];
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];
    public string? Freshness { get; init; }
    public IReadOnlyList<string> SourceReferences { get; init; } = [];
}
