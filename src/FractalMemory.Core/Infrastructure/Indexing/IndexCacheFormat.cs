using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Infrastructure.Indexing;

internal sealed class IndexCacheManifest
{
    public const int CurrentFormatVersion = 3;
    public int FormatVersion { get; set; }
    public DateTimeOffset RefreshedAt { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public Dictionary<string, IndexCacheManifestEntry> Nodes { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class IndexCacheManifestEntry
{
    public string CacheFile { get; set; } = string.Empty;
    public string CacheHash { get; set; } = string.Empty;
    public Dictionary<string, string> FileHashes { get; set; } = new(StringComparer.Ordinal);
    public DateTimeOffset CachedAt { get; set; }
}

internal sealed class NodeCacheDocument
{
    public Dictionary<string, int> SourceStartLines { get; set; } = new(StringComparer.Ordinal);
    public string RelativePath { get; set; } = string.Empty;
    public NodeMetadata Metadata { get; set; } = new();
    public StructuredMemoryFields StateSchema { get; set; } = new();
    public Dictionary<string, string> FileHashes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, CachedMarkdownFile> Files { get; set; } = new(StringComparer.Ordinal);
    public List<CachedEvidence> Evidence { get; set; } = new();
    public string IndexFileName { get; set; } = "index.md";
    public string StateFileName { get; set; } = "state.md";
    public string TimelineFileName { get; set; } = "timeline.md";
    public string DecisionsFileName { get; set; } = "decisions.md";
}

internal sealed class CachedMarkdownFile
{
    public List<CachedSection> Sections { get; set; } = new();
    public string RawContent { get; set; } = string.Empty;
}

internal sealed class CachedSection
{
    public string Heading { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public List<string> Lines { get; set; } = new();
}

internal sealed class CachedEvidence
{
    public string SourcePath { get; set; } = string.Empty;
    public string MatchedFile { get; set; } = string.Empty;
    public string? SectionHeading { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public int Score { get; set; }
}
