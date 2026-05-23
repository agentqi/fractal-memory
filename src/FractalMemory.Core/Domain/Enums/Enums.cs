namespace FractalMemory.Core.Domain.Enums;

public enum RetrievalDepth
{
    Pointer = 0,
    Orientation = 1,
    Working = 2,
    Deep = 3,
}

public enum ExportMode
{
    Compact,
    Standard,
    Verbose,
}

public enum NodeViewType
{
    Index,
    State,
    Timeline,
    Decisions,
    Children,
}

public enum NodeFileFormat
{
    Markdown,
    Html,
}

public enum NodeStatus
{
    Active,
    Paused,
    Archived,
    Draft,
}

public enum PriorityLevel
{
    Low,
    Medium,
    High,
    Critical,
}
