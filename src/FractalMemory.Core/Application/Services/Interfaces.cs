using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Application.Services;

public interface IRepositoryService
{
    Task<string> InitializeAsync(string workingDirectory, CancellationToken cancellationToken);
    string? FindRepositoryRoot(string startDirectory);
    Task<RepositoryConfig> LoadConfigAsync(string repositoryRoot, CancellationToken cancellationToken);
    string GetStorageRoot(string repositoryRoot);
}

public interface INodeService
{
    string NormalizeNodePath(string inputPath);
    Task<MemoryNode> CreateNodeAsync(
        string workingDirectory,
        string nodePath,
        CancellationToken cancellationToken,
        NodeFileFormat format = NodeFileFormat.Markdown,
        IReadOnlyDictionary<string, string>? documents = null);
    Task<MemoryNode> GetNodeAsync(string workingDirectory, string nodePath, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryNode>> GetAllNodesAsync(
        string repositoryRoot, CancellationToken cancellationToken, bool bypassCache = false, string? scope = null);
    Task<IReadOnlyList<MemoryNode>> GetChildNodesAsync(string repositoryRoot, MemoryNode node, CancellationToken cancellationToken);
}

public interface IReadService
{
    Task<OpenNodeResult> OpenAsync(
        string workingDirectory,
        string nodePath,
        RetrievalDepth? depth,
        NodeViewType view,
        CancellationToken cancellationToken);
}

public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string workingDirectory, string query, CancellationToken cancellationToken, int? limit = null, string? scope = null);
    Task<IReadOnlyList<RecentItem>> GetRecentAsync(
        string workingDirectory,
        int limit,
        int days,
        string? scope,
        CancellationToken cancellationToken);
}

public interface IExportService
{
    Task<ExportDocument> ExportAsync(
        string workingDirectory,
        string nodePath,
        ExportMode? mode,
        CancellationToken cancellationToken);
}

public interface IHandoffService
{
    Task<HandoffPacket> CreateAsync(string workingDirectory, string nodePath, CancellationToken cancellationToken);
}

public interface IIndexService
{
    Task RefreshAsync(string workingDirectory, CancellationToken cancellationToken);
}

public interface IStructuredMemoryService
{
    StructuredMemoryFields Parse(string markdown, string fallbackProjectBranch);
    AnswerContextPacket BuildAnswerContext(
        MemoryNode node,
        IReadOnlyList<SearchResult> diagnosticResults,
        int answerTopK,
        int diagnosticTopK);
}

public interface IValidationService
{
    Task<ValidationReport> ValidateAsync(string workingDirectory, CancellationToken cancellationToken);
}

public interface IFileSystemService
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    void MoveDirectory(string sourcePath, string destinationPath);
    void DeleteDirectory(string path);
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);
    IEnumerable<string> EnumerateDirectories(string path);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
    DateTimeOffset GetLastWriteTimeUtc(string path);
    void DeleteFile(string path);
}

public interface IMarkdownFileService
{
    Task<ParsedMarkdownDocument> ReadAsync(string path, CancellationToken cancellationToken);
    Task WriteAsync(string path, NodeMetadata metadata, string content, CancellationToken cancellationToken);
}

public interface IFrontMatterParser
{
    ParsedMarkdownDocument Parse(string markdown);
}

public interface ITemplateService
{
    IReadOnlyDictionary<string, string> GetRepositoryTemplates();
    IReadOnlyDictionary<string, string> GetNodeTemplates(string nodeName, NodeFileFormat format = NodeFileFormat.Markdown);
    Task<IReadOnlyDictionary<string, string>> GetNodeTemplatesAsync(
        string repositoryRoot,
        string nodeName,
        NodeFileFormat format,
        bool includeFrontMatter,
        CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IHumanFormatter
{
    string FormatWorkflow(object value);
    string FormatInitialization(string repositoryRoot);
    string FormatNodeCreated(MemoryNode node);
    string FormatOpen(OpenNodeResult result);
    string FormatSearch(IReadOnlyList<SearchResult> results, string query);
    string FormatRecent(IReadOnlyList<RecentItem> items);
    string FormatValidation(ValidationReport report);
    string FormatHandoff(HandoffPacket packet);
    string FormatIndexesRefreshed();
}

public interface IAiExportFormatter
{
    string Format(ExportDocument document);
}
