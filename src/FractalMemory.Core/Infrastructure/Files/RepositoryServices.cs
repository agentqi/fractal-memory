using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Domain.Rules;
using FractalMemory.Core.Infrastructure.Indexing;
using FractalMemory.Core.Infrastructure.Parsing;
using YamlDotNet.Serialization;

namespace FractalMemory.Core.Infrastructure.Files;

public sealed class RepositoryService(
    IFileSystemService fileSystemService,
    ITemplateService templateService) : IRepositoryService
{
    private static readonly IDeserializer ConfigDeserializer = new DeserializerBuilder().Build();

    public async Task<string> InitializeAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var existing = FindRepositoryRoot(workingDirectory);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Repository already exists at '{existing}'.");
        }

        var repositoryRoot = RepositoryPathGuard.ResolveContainedPath(workingDirectory, ".fractal-memory");
        if (fileSystemService.DirectoryExists(repositoryRoot) || fileSystemService.FileExists(repositoryRoot))
        {
            throw new InvalidOperationException(
                $"Cannot initialize because FractalMem storage already exists at '{repositoryRoot}' without a valid config.yaml.");
        }

        RepositoryPathGuard.CreateContainedDirectory(workingDirectory, ".fractal-memory", fileSystemService);

        foreach (var relativeDirectory in new[]
        {
            "root",
            "root/children",
            "root/artifacts",
            "projects",
            "people",
            "systems",
            "research",
            "handoffs",
            "indexes",
            "templates",
            "templates/node",
            "archive",
        })
        {
            fileSystemService.CreateDirectory(Path.Combine(repositoryRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar)));
        }

        var config = """
            version: 0.1
            default_depth: 1
            default_export_mode: compact
            indexing:
              enabled: true
              refresh_on_write: true
            retrieval:
              diagnostic_top_k: 10
              answer_top_k: 3
              max_snippet_lines: 4
            metadata:
              front_matter: true
            handoffs:
              directory: handoffs
            validation:
              require_index: true
              require_state: true
            """;
        await fileSystemService.WriteAllTextAsync(Path.Combine(repositoryRoot, "config.yaml"), config + Environment.NewLine, cancellationToken);

        foreach (var template in templateService.GetRepositoryTemplates())
        {
            var fullPath = Path.Combine(repositoryRoot, template.Key.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                fileSystemService.CreateDirectory(directory);
            }

            await fileSystemService.WriteAllTextAsync(fullPath, template.Value + Environment.NewLine, cancellationToken);
        }

        await fileSystemService.WriteAllTextAsync(Path.Combine(repositoryRoot, "indexes", "aliases.yaml"), "{}" + Environment.NewLine, cancellationToken);
        await fileSystemService.WriteAllTextAsync(Path.Combine(repositoryRoot, "indexes", "tags.yaml"), "{}" + Environment.NewLine, cancellationToken);
        await fileSystemService.WriteAllTextAsync(Path.Combine(repositoryRoot, "indexes", "paths.yaml"), "{}" + Environment.NewLine, cancellationToken);

        return repositoryRoot;
    }

    public string? FindRepositoryRoot(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ".fractal-memory", "config.yaml");
            if (fileSystemService.FileExists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public async Task<RepositoryConfig> LoadConfigAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var storageRoot = GetStorageRoot(repositoryRoot);
        var configPath = RepositoryPathGuard.ResolveContainedPath(storageRoot, "config.yaml");
        var yaml = await fileSystemService.ReadAllTextAsync(configPath, cancellationToken);
        Dictionary<object, object?> data;
        try { data = ConfigDeserializer.Deserialize<Dictionary<object, object?>>(yaml) ?? []; }
        catch (YamlDotNet.Core.YamlException exception)
        { throw new InvalidOperationException($"Malformed config.yaml: {exception.Message}", exception); }

        var defaultDepthValue = GetInt(data, "default_depth") ?? (int)RetrievalDepth.Orientation;
        if (!Enum.IsDefined(typeof(RetrievalDepth), defaultDepthValue))
        {
            throw new InvalidOperationException("Configuration value 'default_depth' must be between 0 and 3.");
        }

        var defaultExportModeValue = GetString(data, "default_export_mode") ?? ExportMode.Compact.ToString();
        if (!Enum.TryParse<ExportMode>(defaultExportModeValue, true, out var defaultExportMode) || !Enum.IsDefined(defaultExportMode))
        {
            throw new InvalidOperationException("Configuration value 'default_export_mode' must be compact, standard, or verbose.");
        }

        var retrieval = GetSection(data, "retrieval");
        var diagnosticTopK = retrieval is null ? 10 : GetInt(retrieval, "diagnostic_top_k") ?? 10;
        var answerTopK = retrieval is null ? 3 : GetInt(retrieval, "answer_top_k") ?? 3;
        var maxSnippetLines = retrieval is null ? 4 : GetInt(retrieval, "max_snippet_lines") ?? 4;
        if (diagnosticTopK <= 0 || answerTopK <= 0 || maxSnippetLines <= 0)
        {
            throw new InvalidOperationException("Retrieval limits in config.yaml must be positive integers.");
        }

        var handoffs = GetSection(data, "handoffs");
        var handoffDirectory = RepositoryPathGuard.NormalizeRelativeDirectory(
            handoffs is null ? "handoffs" : GetString(handoffs, "directory") ?? "handoffs");

        return new RepositoryConfig
        {
            Version = GetString(data, "version") ?? "0.1",
            DefaultDepth = (RetrievalDepth)defaultDepthValue,
            DefaultExportMode = defaultExportMode,
            Indexing = GetSection(data, "indexing") is { } indexing ? new IndexingOptions
            {
                Enabled = GetBool(indexing, "enabled") ?? true,
                RefreshOnWrite = GetBool(indexing, "refresh_on_write") ?? true,
            } : new IndexingOptions(),
            Retrieval = new RetrievalOptions
            {
                DiagnosticTopK = diagnosticTopK,
                AnswerTopK = answerTopK,
                MaxSnippetLines = maxSnippetLines,
            },
            Metadata = GetSection(data, "metadata") is { } metadata ? new MetadataOptions
            {
                FrontMatter = GetBool(metadata, "front_matter") ?? true,
            } : new MetadataOptions(),
            Handoffs = new HandoffOptions
            {
                Directory = handoffDirectory,
            },
            Validation = GetSection(data, "validation") is { } validation ? new ValidationOptions
            {
                RequireIndex = GetBool(validation, "require_index") ?? true,
                RequireState = GetBool(validation, "require_state") ?? true,
            } : new ValidationOptions(),
        };
    }

    public string GetStorageRoot(string repositoryRoot) =>
        RepositoryPathGuard.ResolveContainedPath(repositoryRoot, ".fractal-memory");

    private static IReadOnlyDictionary<object, object?>? GetSection(IReadOnlyDictionary<object, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var section)) return null;
        return section as IReadOnlyDictionary<object, object?>
            ?? throw new InvalidOperationException($"Configuration section '{key}' must be a mapping.");
    }

    private static string? GetString(IReadOnlyDictionary<object, object?> source, string key) =>
        source.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool? GetBool(IReadOnlyDictionary<object, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value)) return null;
        return bool.TryParse(value?.ToString(), out var parsed) ? parsed
            : throw new InvalidOperationException($"Configuration value '{key}' must be true or false.");
    }

    private static int? GetInt(IReadOnlyDictionary<object, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value)) return null;
        return int.TryParse(value?.ToString(), out var parsed) ? parsed
            : throw new InvalidOperationException($"Configuration value '{key}' must be an integer.");
    }

}

public sealed class NodeService(
    IRepositoryService repositoryService,
    IFileSystemService fileSystemService,
    IMarkdownFileService markdownFileService,
    ITemplateService templateService,
    INodeListingCacheReader cacheReader) : INodeService
{
    private readonly ConcurrentDictionary<string, NodeListingCacheEntry> _nodeListingCache = new(StringComparer.Ordinal);

    public string NormalizeNodePath(string inputPath) => NodePathRules.Normalize(inputPath);

    private sealed record NodeListingCacheEntry(string Fingerprint, IReadOnlyList<MemoryNode> Nodes);

    public async Task<MemoryNode> CreateNodeAsync(
        string workingDirectory,
        string nodePath,
        CancellationToken cancellationToken,
        NodeFileFormat format = NodeFileFormat.Markdown,
        IReadOnlyDictionary<string, string>? documents = null)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), "Node format must be Markdown or Html.");
        }

        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");

        var normalizedPath = NormalizeNodePath(nodePath);
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var fullPath = RepositoryPathGuard.ResolveContainedPath(storageRoot, normalizedPath);
        if (fileSystemService.DirectoryExists(fullPath))
        {
            throw new InvalidOperationException($"Node '{normalizedPath}' already exists.");
        }

        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var templates = await templateService.GetNodeTemplatesAsync(
            repositoryRoot,
            Path.GetFileName(normalizedPath),
            format,
            config.Metadata.FrontMatter,
            cancellationToken);
        if (documents is not null && format != NodeFileFormat.Markdown)
            throw new ArgumentException("Imported document overrides require Markdown node templates.");
        var allDocuments = new Dictionary<string, string>(templates, StringComparer.Ordinal);
        foreach (var document in documents ?? new Dictionary<string, string>())
        {
            if (document.Key != "index.md" && !Regex.IsMatch(document.Key, @"^artifacts/source\.(md|html|htm|txt)$"))
                throw new ArgumentException("Creation overrides support index.md and artifacts/source documents only.");
            allDocuments[document.Key] = document.Value;
        }
        var stagingRelativePath = $".staging/{Guid.NewGuid():N}";
        var stagingPath = RepositoryPathGuard.ResolveContainedPath(storageRoot, stagingRelativePath);
        try
        {
            fileSystemService.CreateDirectory(stagingPath);
            fileSystemService.CreateDirectory(Path.Combine(stagingPath, "children"));
            fileSystemService.CreateDirectory(Path.Combine(stagingPath, "artifacts"));
            foreach (var template in allDocuments)
            {
                var destination = RepositoryPathGuard.ResolveContainedPath(stagingPath, template.Key);
                await fileSystemService.WriteAllTextAsync(destination, template.Value, cancellationToken);
            }

            // Parse the entire staged node before making it visible to repository readers.
            await LoadNodeAsync(repositoryRoot, stagingRelativePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            fileSystemService.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            // Recheck after creating ancestors: another process may have replaced one with a link.
            RepositoryPathGuard.EnsureContainedPath(storageRoot, fullPath);
            fileSystemService.MoveDirectory(stagingPath, fullPath);
        }
        finally
        {
            if (fileSystemService.DirectoryExists(stagingPath))
            {
                fileSystemService.DeleteDirectory(stagingPath);
            }
        }

        return await LoadNodeAsync(repositoryRoot, normalizedPath, cancellationToken);
    }

    public async Task<MemoryNode> GetNodeAsync(string workingDirectory, string nodePath, CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var normalizedPath = NormalizeNodePath(nodePath);
        return await LoadNodeAsync(repositoryRoot, normalizedPath, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryNode>> GetAllNodesAsync(
        string repositoryRoot, CancellationToken cancellationToken, bool bypassCache = false, string? scope = null)
    {
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var scopedRoot = scope is null ? storageRoot : RepositoryPathGuard.ResolveContainedPath(storageRoot, scope);
        var useCache = !bypassCache && scope is null && config.Indexing.Enabled;
        var fingerprint = useCache ? ComputeNodeFingerprintForCache(fileSystemService, storageRoot, config.Handoffs.Directory) : null;
        var cacheKey = Path.GetFullPath(repositoryRoot);
        if (bypassCache) _nodeListingCache.TryRemove(cacheKey, out _);
        if (useCache && _nodeListingCache.TryGetValue(cacheKey, out var cached) && string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return cached.Nodes;
        }

        var fromDiskCache = useCache ? await cacheReader.TryLoadAsync(repositoryRoot, fingerprint!, cancellationToken) : null;
        if (fromDiskCache is not null)
        {
            _nodeListingCache[cacheKey] = new NodeListingCacheEntry(fingerprint!, fromDiskCache);
            return fromDiskCache;
        }

        var directories = EnumerateNodeIndexFiles(scopedRoot)
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(path => !IsExcludedNodeContent(storageRoot, path, config.Handoffs.Directory))
            .ToArray();

        var nodes = new List<MemoryNode>(directories.Length);
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(storageRoot, directory).Replace(Path.DirectorySeparatorChar, '/');
            nodes.Add(await LoadNodeAsync(repositoryRoot, relativePath, cancellationToken));
        }

        var ordered = nodes.OrderBy(node => node.RelativePath, StringComparer.Ordinal).ToArray();
        if (useCache)
        {
            _nodeListingCache[cacheKey] = new NodeListingCacheEntry(fingerprint!, ordered);
        }
        return ordered;
    }

    internal static string ComputeNodeFingerprintForCache(IFileSystemService fileSystem, string storageRoot, string handoffDirectory = "handoffs")
    {
        var entries = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var file in EnumerateNodeFingerprintFilesStatic(fileSystem, storageRoot, handoffDirectory))
        {
            RepositoryPathGuard.EnsureContainedPath(storageRoot, file);
            var relative = Path.GetRelativePath(storageRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            entries[relative] = fileSystem.GetLastWriteTimeUtc(file).UtcTicks;
        }

        if (entries.Count == 0)
        {
            return "0:empty";
        }

        var builder = new StringBuilder();
        foreach (var (path, mtime) in entries)
        {
            builder.Append(path).Append('|').Append(mtime).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return $"{entries.Count}:{Convert.ToHexString(hash)}";
    }

    private static IEnumerable<string> EnumerateNodeFingerprintFilesStatic(IFileSystemService fileSystem, string storageRoot, string handoffDirectory)
    {
        foreach (var path in fileSystem.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories))
        {
            if (!IsContentExtension(path))
            {
                continue;
            }

            if (IsExcludedNodeContent(storageRoot, path, handoffDirectory))
            {
                continue;
            }

            yield return path;
        }
    }

    internal static bool IsExcludedNodeContent(string storageRoot, string path, string handoffDirectory)
    {
        var relative = Path.GetRelativePath(storageRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return new[] { "templates", "archive", "handoffs", "indexes", ".staging", handoffDirectory }
            .Any(prefix => relative == prefix || relative.StartsWith(prefix + "/", StringComparison.Ordinal)) ||
            relative.Split('/').Contains("artifacts", StringComparer.Ordinal);
    }

    internal static bool IsContentExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<MemoryNode>> GetChildNodesAsync(string repositoryRoot, MemoryNode node, CancellationToken cancellationToken)
    {
        var childrenDirectory = Path.Combine(node.FullPath, "children");
        RepositoryPathGuard.EnsureContainedPath(repositoryService.GetStorageRoot(repositoryRoot), childrenDirectory);
        var children = new List<MemoryNode>();
        foreach (var childDirectory in fileSystemService.EnumerateDirectories(childrenDirectory))
        {
            if (!HasNodeFile(childDirectory, "index"))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(repositoryService.GetStorageRoot(repositoryRoot), childDirectory)
                .Replace(Path.DirectorySeparatorChar, '/');
            children.Add(await LoadNodeAsync(repositoryRoot, relativePath, cancellationToken));
        }

        return children.OrderBy(child => child.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private async Task<MemoryNode> LoadNodeAsync(string repositoryRoot, string normalizedPath, CancellationToken cancellationToken)
    {
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var fullPath = RepositoryPathGuard.ResolveContainedPath(storageRoot, normalizedPath);
        if (!fileSystemService.DirectoryExists(fullPath))
        {
            throw new InvalidOperationException($"Node '{normalizedPath}' does not exist.");
        }

        var topLevel = SnapshotTopLevelFiles(fullPath);
        var indexPath = ResolveFromSnapshot(topLevel, fullPath, "index")
            ?? throw new InvalidOperationException($"Node '{normalizedPath}' is missing index.md or index.html.");
        var statePath = ResolveFromSnapshot(topLevel, fullPath, "state");
        var timelinePath = ResolveFromSnapshot(topLevel, fullPath, "timeline");
        var decisionsPath = ResolveFromSnapshot(topLevel, fullPath, "decisions");
        var index = await ReadNodeDocumentAsync(indexPath, cancellationToken);
        var state = statePath is not null
            ? await ReadNodeDocumentAsync(statePath, cancellationToken)
            : new ParsedMarkdownDocument { Metadata = new NodeMetadata(), Content = string.Empty };
        var timeline = timelinePath is not null
            ? await ReadNodeDocumentAsync(timelinePath, cancellationToken)
            : new ParsedMarkdownDocument { Metadata = new NodeMetadata(), Content = string.Empty };
        var decisions = decisionsPath is not null
            ? await ReadNodeDocumentAsync(decisionsPath, cancellationToken)
            : new ParsedMarkdownDocument { Metadata = new NodeMetadata(), Content = string.Empty };

        return new MemoryNode
        {
            RelativePath = normalizedPath,
            FullPath = fullPath,
            Metadata = MergeMetadata(index.Metadata, state.Metadata, timeline.Metadata, decisions.Metadata),
            IndexFileName = Path.GetFileName(indexPath),
            StateFileName = statePath is null ? "state.md" : Path.GetFileName(statePath),
            TimelineFileName = timelinePath is null ? "timeline.md" : Path.GetFileName(timelinePath),
            DecisionsFileName = decisionsPath is null ? "decisions.md" : Path.GetFileName(decisionsPath),
            IndexContent = index.Content,
            StateContent = state.Content,
            TimelineContent = timeline.Content,
            DecisionsContent = decisions.Content,
            SourceHashes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Path.GetFileName(indexPath)] = index.SourceHash,
                [statePath is null ? "state.md" : Path.GetFileName(statePath)] = state.SourceHash,
                [timelinePath is null ? "timeline.md" : Path.GetFileName(timelinePath)] = timeline.SourceHash,
                [decisionsPath is null ? "decisions.md" : Path.GetFileName(decisionsPath)] = decisions.SourceHash,
            },
            SourceStartLines = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [Path.GetFileName(indexPath)] = index.ContentStartLine,
                [statePath is null ? "state.md" : Path.GetFileName(statePath)] = state.ContentStartLine,
                [timelinePath is null ? "timeline.md" : Path.GetFileName(timelinePath)] = timeline.ContentStartLine,
                [decisionsPath is null ? "decisions.md" : Path.GetFileName(decisionsPath)] = decisions.ContentStartLine,
            },
        };
    }

    private IEnumerable<string> EnumerateNodeIndexFiles(string storageRoot)
    {
        foreach (var file in fileSystemService.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("index.md", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("index.htm", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private bool HasNodeFile(string directory, string baseName) => ResolveNodeFile(directory, baseName) is not null;

    private HashSet<string> SnapshotTopLevelFiles(string directory)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in fileSystemService.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            names.Add(Path.GetFileName(file));
        }

        return names;
    }

    private static string? ResolveFromSnapshot(HashSet<string> topLevel, string directory, string baseName)
    {
        foreach (var extension in new[] { ".md", ".html", ".htm" })
        {
            var fileName = baseName + extension;
            if (topLevel.Contains(fileName))
            {
                return Path.Combine(directory, fileName);
            }
        }

        return null;
    }

    private string? ResolveNodeFile(string directory, string baseName)
    {
        foreach (var extension in new[] { ".md", ".html", ".htm" })
        {
            var path = Path.Combine(directory, baseName + extension);
            if (fileSystemService.FileExists(path))
            {
                return path;
            }
        }

        return null;
    }

    private async Task<ParsedMarkdownDocument> ReadNodeDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            var html = await fileSystemService.ReadAllTextAsync(path, cancellationToken);
            var title = HtmlTextExtractor.ExtractTitle(html);
            return new ParsedMarkdownDocument
            {
                Metadata = new NodeMetadata { Title = title },
                Content = HtmlTextExtractor.ToText(html),
                SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))),
            };
        }

        return await markdownFileService.ReadAsync(path, cancellationToken);
    }

    private static NodeMetadata MergeMetadata(NodeMetadata primary, NodeMetadata secondary, NodeMetadata timeline, NodeMetadata decisions) =>
        new()
        {
            Title = primary.Title ?? secondary.Title,
            Aliases = primary.Aliases.Count > 0 ? primary.Aliases : secondary.Aliases,
            Tags = primary.Tags.Count > 0 ? primary.Tags : secondary.Tags,
            Status = primary.Status,
            Priority = primary.Priority,
            LastUpdated = new[] { primary.LastUpdated, secondary.LastUpdated, timeline.LastUpdated, decisions.LastUpdated }.Max(),
            ReviewAfter = primary.ReviewAfter,
            Owner = primary.Owner ?? secondary.Owner,
            Summary = primary.Summary ?? secondary.Summary,
        };
}

public sealed class ReadService(
    IRepositoryService repositoryService,
    INodeService nodeService,
    IStructuredMemoryService structuredMemoryService) : IReadService
{
    public async Task<OpenNodeResult> OpenAsync(
        string workingDirectory,
        string nodePath,
        RetrievalDepth? depth,
        NodeViewType view,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        depth ??= (await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken)).DefaultDepth;
        if (!Enum.IsDefined(depth.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Retrieval depth must be between 0 and 3.");
        }
        if (!Enum.IsDefined(view))
        {
            throw new ArgumentOutOfRangeException(nameof(view), "Unknown node view.");
        }

        var node = await nodeService.GetNodeAsync(repositoryRoot, nodePath, cancellationToken);
        var children = await nodeService.GetChildNodesAsync(repositoryRoot, node, cancellationToken);
        var structuredState = structuredMemoryService.Parse(node.StateContent, node.RelativePath);

        var title = node.Metadata.Title ?? Path.GetFileName(node.RelativePath);
        var summary = node.Metadata.Summary
            ?? structuredState.CurrentObjective
            ?? ExtractSummary(node.IndexContent)
            ?? ExtractSummary(node.StateContent)
            ?? "No summary available.";

        var showIndex = depth >= RetrievalDepth.Orientation && view is NodeViewType.Index or NodeViewType.Children;
        var showState = depth >= RetrievalDepth.Working || view == NodeViewType.State;
        var showTimeline = depth >= RetrievalDepth.Deep || view == NodeViewType.Timeline;
        var showDecisions = depth >= RetrievalDepth.Deep || view == NodeViewType.Decisions;
        var currentState = showState
            ? depth == RetrievalDepth.Deep || view == NodeViewType.State
                ? node.StateContent
                : SummarizeWorkingState(node.StateContent)
            : null;

        return new OpenNodeResult
        {
            RelativePath = node.RelativePath,
            Title = title,
            Summary = summary,
            Depth = depth.Value,
            View = view,
            IndexSummary = showIndex
                ? TrimToParagraphs(node.IndexContent, depth == RetrievalDepth.Working ? 2 : 3)
                : null,
            CurrentState = currentState,
            StateTruncated = showState && currentState?.Trim() != node.StateContent.Trim(),
            Children = children.Select(child => $"{child.RelativePath} - {child.Metadata.Summary ?? ExtractSummary(child.IndexContent) ?? child.Metadata.Title ?? Path.GetFileName(child.RelativePath)}").ToArray(),
            SuggestedReads = BuildSuggestedReads(node, children),
            RecentTimeline = showTimeline ? ExtractHighlights(node.TimelineContent, 3) : [],
            RecentDecisions = showDecisions ? ExtractHighlights(node.DecisionsContent, 3) : [],
        };
    }

    private static string SummarizeWorkingState(string content)
    {
        var sections = FractalMemory.Core.Infrastructure.Search.RetrievalPipeline.ParseSections(content);
        var priorities = new[] { "Current Objective", "Current Goal", "Current State", "Active Constraints",
            "Next Best Actions", "Key Decisions in Force", "Open Questions" };
        var selected = sections
            .Where(section => priorities.Contains(section.Heading, StringComparer.OrdinalIgnoreCase) &&
                section.Lines.Any(line => !string.IsNullOrWhiteSpace(line)))
            .OrderBy(section => Array.FindIndex(priorities, heading => heading.Equals(section.Heading, StringComparison.OrdinalIgnoreCase)))
            .Take(3)
            .Select(section => $"## {section.Heading}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, section.Lines).Trim()}")
            .ToArray();
        return selected.Length == 0 ? TrimToParagraphs(content, 2) : string.Join(Environment.NewLine + Environment.NewLine, selected);
    }

    private static IReadOnlyList<string> BuildSuggestedReads(MemoryNode node, IReadOnlyList<MemoryNode> children)
    {
        var suggestions = new List<string> { $"{node.RelativePath}/{node.StateFileName}" };
        if (!string.IsNullOrWhiteSpace(node.DecisionsContent))
        {
            suggestions.Add($"{node.RelativePath}/{node.DecisionsFileName}");
        }

        suggestions.AddRange(children.Take(3).Select(child => child.RelativePath));
        return suggestions.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? ExtractSummary(string content) =>
        content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('#', '-', '*', ' '))
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

    internal static string TrimToParagraphs(string content, int maxParagraphs)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs.Take(maxParagraphs)).Trim();
    }

    internal static IReadOnlyList<string> ExtractHighlights(string content, int maxItems)
    {
        var candidates = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((line, index) => (Line: line.Trim(), Index: index))
            .Where(item => !item.Line.StartsWith('#'))
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        var dated = candidates
            .Select(item => (item.Line, item.Index, Date: TryExtractFirstDate(item.Line)))
            .Where(item => item.Date is not null)
            .ToArray();

        if (dated.Length >= maxItems)
        {
            return dated
                .OrderByDescending(item => item.Date!.Value)
                .ThenByDescending(item => item.Index)
                .Take(maxItems)
                .Select(item => item.Line)
                .ToArray();
        }

        return candidates.TakeLast(maxItems).Select(item => item.Line).ToArray();
    }

    private static DateOnly? TryExtractFirstDate(string line)
    {
        foreach (var date in FractalMemory.Core.Infrastructure.Search.RetrievalPipeline.ExtractDatesForHighlight(line))
        {
            return date;
        }

        return null;
    }
}

public sealed class ExportService(
    IReadService readService,
    IRepositoryService repositoryService,
    INodeService nodeService,
    IStructuredMemoryService structuredMemoryService) : IExportService
{
    public async Task<ExportDocument> ExportAsync(
        string workingDirectory,
        string nodePath,
        ExportMode? mode,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        mode ??= config.DefaultExportMode;
        if (!Enum.IsDefined(mode.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Export mode must be Compact, Standard, or Verbose.");
        }

        var depth = mode switch
        {
            ExportMode.Compact => RetrievalDepth.Orientation,
            ExportMode.Standard => RetrievalDepth.Deep,
            ExportMode.Verbose => RetrievalDepth.Deep,
            _ => RetrievalDepth.Orientation,
        };

        var node = await nodeService.GetNodeAsync(repositoryRoot, nodePath, cancellationToken);
        var opened = await readService.OpenAsync(workingDirectory, nodePath, depth, NodeViewType.Index, cancellationToken);
        var evidence = FractalMemory.Core.Infrastructure.Search.SearchService.BuildNodeEvidence(node, config.Retrieval.MaxSnippetLines);
        var answerContext = structuredMemoryService.BuildAnswerContext(
            node,
            evidence,
            config.Retrieval.AnswerTopK,
            config.Retrieval.DiagnosticTopK);

        return new ExportDocument
        {
            RelativePath = opened.RelativePath,
            Title = opened.Title,
            Mode = mode.Value,
            Summary = opened.Summary,
            CurrentState = opened.CurrentState ?? string.Empty,
            Children = opened.Children,
            TimelineHighlights = opened.RecentTimeline,
            DecisionHighlights = opened.RecentDecisions,
            AnswerContext = answerContext,
        };
    }
}

public sealed class HandoffService(
    IRepositoryService repositoryService,
    IReadService readService,
    INodeService nodeService,
    IFileSystemService fileSystemService,
    IClock clock,
    IStructuredMemoryService structuredMemoryService) : IHandoffService
{
    public async Task<HandoffPacket> CreateAsync(string workingDirectory, string nodePath, CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var node = await nodeService.GetNodeAsync(workingDirectory, nodePath, cancellationToken);
        var opened = await readService.OpenAsync(workingDirectory, nodePath, RetrievalDepth.Deep, NodeViewType.Index, cancellationToken);
        var evidence = FractalMemory.Core.Infrastructure.Search.SearchService.BuildNodeEvidence(node, config.Retrieval.MaxSnippetLines);
        var answerContext = structuredMemoryService.BuildAnswerContext(
            node,
            evidence,
            config.Retrieval.AnswerTopK,
            config.Retrieval.DiagnosticTopK);
        var structuredState = structuredMemoryService.Parse(node.StateContent, node.RelativePath);

        var currentGoal = answerContext.CurrentObjective ?? opened.Summary;
        var currentState = opened.CurrentState ?? "No current state captured.";
        var openQuestions = structuredState.OpenQuestions.Count > 0
            ? structuredState.OpenQuestions
            : ExtractQuestions(currentState);
        var nextActions = answerContext.NextBestActions.Count > 0 ? answerContext.NextBestActions : BuildNextActions(opened);
        var readFirst = answerContext.SupportingSources.Select(source => source.SourcePath).Distinct(StringComparer.Ordinal).Concat(new[]
        {
            $"{opened.RelativePath}/{node.IndexFileName}",
            $"{opened.RelativePath}/{node.StateFileName}",
            $"{opened.RelativePath}/{node.DecisionsFileName}",
        }).Distinct(StringComparer.Ordinal).ToArray();
        var sourceReferences = answerContext.SupportingSources
            .Select(source =>
            {
                var range = source.StartLine is not null && source.EndLine is not null ? $"#L{source.StartLine}-{source.EndLine}" : string.Empty;
                return $"- `{source.SourcePath}{range}`";
            })
            .ToArray();
        var freshness = node.Metadata.LastUpdated?.ToString("O", CultureInfo.InvariantCulture) ?? "Unknown";

        var timestamp = clock.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        var safeName = opened.RelativePath.Replace('/', '-');
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var handoffDirectory = RepositoryPathGuard.ResolveContainedPath(storageRoot, config.Handoffs.Directory);
        RepositoryPathGuard.CreateContainedDirectory(storageRoot, config.Handoffs.Directory, fileSystemService);
        var fileName = $"{timestamp}-{safeName}-{Guid.NewGuid():N}.md";
        var fullPath = RepositoryPathGuard.ResolveContainedPath(handoffDirectory, fileName);

        var content = new StringBuilder()
            .AppendLine($"# Handoff: {opened.Title}")
            .AppendLine()
            .AppendLine("## Project / Branch")
            .AppendLine()
            .AppendLine(answerContext.ProjectBranch)
            .AppendLine()
            .AppendLine("## Current Objective")
            .AppendLine()
            .AppendLine(currentGoal)
            .AppendLine()
            .AppendLine("## Current State")
            .AppendLine()
            .AppendLine(currentState)
            .AppendLine()
            .AppendLine("## Key Decisions in Force")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, answerContext.KeyPriorDecisions.DefaultIfEmpty("- None recorded.")))
            .AppendLine()
            .AppendLine("## Active Constraints")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, answerContext.ActiveConstraints.DefaultIfEmpty("- None captured.")))
            .AppendLine()
            .AppendLine("## Open Questions")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, openQuestions.DefaultIfEmpty("- No explicit open questions extracted.")))
            .AppendLine()
            .AppendLine("## Next Best Actions")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, nextActions))
            .AppendLine()
            .AppendLine("## Last Updated / Freshness")
            .AppendLine()
            .AppendLine(freshness)
            .AppendLine()
            .AppendLine("## Source References")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, sourceReferences.DefaultIfEmpty("- No explicit supporting sources.")))
            .AppendLine()
            .AppendLine("## Read First Files")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, readFirst.Select(path => $"- `{path}`")))
            .ToString();

        content = MemoryMarkdown.SetMetadata(content, new Dictionary<string, object?>
        {
            ["node_path"] = node.RelativePath,
            ["created_at"] = clock.UtcNow.ToString("O"),
            ["source_hashes"] = node.SourceHashes,
        });
        await fileSystemService.WriteAllTextAsync(fullPath, content, cancellationToken);

        return new HandoffPacket
        {
            RelativePath = opened.RelativePath,
            HandoffFilePath = Path.GetRelativePath(repositoryRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/'),
            ProjectBranch = answerContext.ProjectBranch,
            CurrentGoal = currentGoal,
            CurrentState = currentState,
            RecentDecisions = answerContext.KeyPriorDecisions,
            ActiveConstraints = answerContext.ActiveConstraints,
            OpenQuestions = openQuestions,
            NextBestActions = nextActions,
            ReadFirstFiles = readFirst,
            SourceReferences = sourceReferences,
            RenderedContent = content,
        };
    }

    private static IReadOnlyList<string> ExtractQuestions(string content)
    {
        var questions = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains('?', StringComparison.Ordinal))
            .Select(line => $"- {line.TrimStart('-', '*', ' ')}")
            .ToArray();

        return questions;
    }

    private static IReadOnlyList<string> BuildNextActions(OpenNodeResult opened)
    {
        var actions = new List<string>
        {
            "- Review `state.md` and confirm it still reflects current truth.",
        };

        if (opened.RecentDecisions.Count == 0)
        {
            actions.Add("- Capture any implicit decisions in `decisions.md`.");
        }

        if (opened.Children.Count > 0)
        {
            actions.Add("- Inspect the most relevant child node before making changes.");
        }

        return actions;
    }
}

public sealed class ValidationService(
    IRepositoryService repositoryService,
    IFileSystemService fileSystemService,
    IMarkdownFileService markdownFileService) : IValidationService
{
    private static readonly string[] NodeContractFileNames = ["index", "state", "timeline", "decisions"];
    private static readonly string[] NodeFileExtensions = [".md", ".html", ".htm"];

    public async Task<ValidationReport> ValidateAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        try { return await ValidateCoreAsync(workingDirectory, cancellationToken); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ValidationReport { Issues = [new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                RelativePath = ".",
                Message = $"Could not inspect repository storage: {exception.Message} Check file and directory permissions.",
            }] };
        }
    }

    private async Task<ValidationReport> ValidateCoreAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var issues = new List<ValidationIssue>();
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        foreach (var reparsePoint in RepositoryPathGuard.EnumerateReparsePoints(storageRoot))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                RelativePath = Path.GetRelativePath(storageRoot, reparsePoint).Replace(Path.DirectorySeparatorChar, '/'),
                Message = "Symbolic links and reparse points are not allowed inside FractalMem storage.",
            });
        }

        var memoryFiles = EnumerateMemoryFiles(storageRoot)
            .Where(path => !NodeService.IsExcludedNodeContent(storageRoot, path, config.Handoffs.Directory))
            .ToArray();
        var filesByDirectory = memoryFiles
            .GroupBy(path => Path.GetDirectoryName(path), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key!,
                group => BuildNodeFileMap(group),
                StringComparer.Ordinal);
        foreach (var directory in EnumerateNodeScaffolds(storageRoot, config.Handoffs.Directory))
        {
            filesByDirectory.TryAdd(directory, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
        var candidateDirectories = filesByDirectory
            .Where(pair => pair.Value.Count > 0 ||
                fileSystemService.DirectoryExists(Path.Combine(pair.Key, "children")) ||
                fileSystemService.DirectoryExists(Path.Combine(pair.Key, "artifacts")))
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var directory in candidateDirectories)
        {
            var relativePath = Path.GetRelativePath(storageRoot, directory).Replace(Path.DirectorySeparatorChar, '/');
            try
            {
                NodePathRules.Validate(relativePath);
            }
            catch (Exception exception)
            {
                issues.Add(new ValidationIssue { Severity = ValidationSeverity.Error, RelativePath = relativePath, Message = exception.Message });
            }

            var nodeFiles = filesByDirectory[directory];
            nodeFiles.TryGetValue("index", out var indexPath);
            nodeFiles.TryGetValue("state", out var statePath);
            nodeFiles.TryGetValue("timeline", out var timelinePath);
            nodeFiles.TryGetValue("decisions", out var decisionsPath);
            if (config.Validation.RequireIndex && indexPath is null)
            {
                issues.Add(new ValidationIssue { Severity = ValidationSeverity.Error, RelativePath = relativePath, Message = "Missing required index.md or index.html." });
            }

            if (config.Validation.RequireState && statePath is null)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    RelativePath = relativePath,
                    Message = "Missing required state.md or state.html. Search and listings will include the node with empty state until state is present.",
                });
            }

            ParsedMarkdownDocument? parsedIndex = null;
            foreach (var memoryFile in new[] { indexPath, statePath, timelinePath, decisionsPath })
            {
                if (memoryFile is null)
                {
                    continue;
                }

                try
                {
                    var document = await ReadMemoryFileAsync(memoryFile, cancellationToken);
                    if (string.Equals(memoryFile, indexPath, StringComparison.Ordinal))
                    {
                        parsedIndex = document;
                    }

                    if (Path.GetFileNameWithoutExtension(memoryFile).Equals("state", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(document.Content))
                    {
                        issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, RelativePath = relativePath, Message = $"{Path.GetFileName(memoryFile)} is empty." });
                    }

                    var isMarkdown = Path.GetExtension(memoryFile).Equals(".md", StringComparison.OrdinalIgnoreCase);
                    if ((config.Metadata.FrontMatter || !isMarkdown) && string.IsNullOrWhiteSpace(document.Metadata.Title))
                    {
                        issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, RelativePath = relativePath, Message = $"{Path.GetFileName(memoryFile)} is missing a title." });
                    }

                    if (config.Metadata.FrontMatter && isMarkdown && document.Metadata.LastUpdated is null)
                    {
                        issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, RelativePath = relativePath, Message = $"{Path.GetFileName(memoryFile)} is missing last_updated." });
                    }
                }
                catch (Exception exception)
                {
                    issues.Add(new ValidationIssue { Severity = ValidationSeverity.Error, RelativePath = relativePath, Message = $"{Path.GetFileName(memoryFile)} has malformed content: {exception.Message}" });
                }
            }

            if (indexPath is not null)
            {
                try
                {
                    var indexDocument = parsedIndex ?? await ReadMemoryFileAsync(indexPath, cancellationToken);
                    var referencedChildren = Regex.Matches(indexDocument.Content, @"children/([a-z0-9/-]+)")
                        .Select(match => match.Groups[1].Value)
                        .Distinct(StringComparer.Ordinal);
                    foreach (var child in referencedChildren)
                    {
                        var childPath = Path.Combine(directory, "children", child.Replace('/', Path.DirectorySeparatorChar));
                        if (!fileSystemService.DirectoryExists(childPath))
                        {
                            issues.Add(new ValidationIssue
                            {
                                Severity = ValidationSeverity.Warning,
                                RelativePath = relativePath,
                                Message = $"Index references missing child 'children/{child}'.",
                            });
                        }
                    }
                }
                catch
                {
                    // Front matter parse errors are already reported above.
                }
            }
        }

        var duplicates = candidateDirectories
            .Select(directory => Path.GetRelativePath(storageRoot, directory).Replace(Path.DirectorySeparatorChar, '/'))
            .GroupBy(path => path.ToLowerInvariant())
            .Where(group => group.Count() > 1);
        foreach (var duplicate in duplicates)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                RelativePath = duplicate.Key,
                Message = "Duplicate canonical path discovered.",
            });
        }

        var latestNodeWrite = memoryFiles
            .Select(fileSystemService.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        var indexesRoot = Path.Combine(storageRoot, "indexes");
        var latestIndexWrite = fileSystemService.EnumerateFiles(indexesRoot, "*.yaml", SearchOption.TopDirectoryOnly)
            .Select(fileSystemService.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
        if (latestNodeWrite > latestIndexWrite)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Warning,
                RelativePath = "indexes",
                Message = "Indexes may be stale. Run 'fm index refresh'." +
                    (config.Indexing.Enabled && !config.Indexing.RefreshOnWrite
                        ? " Set indexing.refresh_on_write: true in config.yaml to refresh automatically after writes; older templates explicitly disabled this setting." : ""),
            });
        }

        return new ValidationReport { Issues = issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.RelativePath, StringComparer.Ordinal).ToArray() };
    }

    private IEnumerable<string> EnumerateMemoryFiles(string storageRoot)
    {
        foreach (var path in fileSystemService.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories))
        {
            if (NodeService.IsContentExtension(path))
            {
                yield return path;
            }
        }
    }

    private IEnumerable<string> EnumerateNodeScaffolds(string storageRoot, string handoffDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(storageRoot);
        while (pending.TryPop(out var directory))
        {
            var children = fileSystemService.EnumerateDirectories(directory).ToArray();
            if (children.Any(child => Path.GetFileName(child) is "children" or "artifacts"))
            {
                yield return directory;
            }

            foreach (var child in children)
            {
                if (!NodeService.IsExcludedNodeContent(storageRoot, child, handoffDirectory))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static Dictionary<string, string> BuildNodeFileMap(IEnumerable<string> files)
    {
        var candidates = files.ToArray();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseName in NodeContractFileNames)
        {
            foreach (var extension in NodeFileExtensions)
            {
                var match = candidates.FirstOrDefault(path =>
                    Path.GetFileName(path).Equals(baseName + extension, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    map[baseName] = match;
                    break;
                }
            }
        }

        return map;
    }

    private async Task<ParsedMarkdownDocument> ReadMemoryFileAsync(string path, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            var html = await fileSystemService.ReadAllTextAsync(path, cancellationToken);
            return new ParsedMarkdownDocument
            {
                Metadata = new NodeMetadata { Title = HtmlTextExtractor.ExtractTitle(html) },
                Content = HtmlTextExtractor.ToText(html),
            };
        }

        return await markdownFileService.ReadAsync(path, cancellationToken);
    }
}
