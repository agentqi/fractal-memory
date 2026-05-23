using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Domain.Rules;
using YamlDotNet.Serialization;

namespace FractalMemory.Core.Infrastructure.Files;

public sealed class RepositoryService(
    IFileSystemService fileSystemService,
    ITemplateService templateService) : IRepositoryService
{
    public async Task<string> InitializeAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var existing = FindRepositoryRoot(workingDirectory);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Repository already exists at '{existing}'.");
        }

        var repositoryRoot = Path.Combine(workingDirectory, ".fractal-memory");
        fileSystemService.CreateDirectory(repositoryRoot);

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
            root_dir: .fractal-memory
            default_depth: 1
            default_export_mode: compact
            indexing:
              enabled: true
              refresh_on_write: false
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
        var configPath = Path.Combine(GetStorageRoot(repositoryRoot), "config.yaml");
        var yaml = await fileSystemService.ReadAllTextAsync(configPath, cancellationToken);
        var deserializer = new DeserializerBuilder().Build();
        var data = deserializer.Deserialize<Dictionary<object, object?>>(yaml) ?? [];

        return new RepositoryConfig
        {
            Version = GetString(data, "version") ?? "0.1",
            RootDir = GetString(data, "root_dir") ?? ".fractal-memory",
            DefaultDepth = (RetrievalDepth)(GetInt(data, "default_depth") ?? 1),
            DefaultExportMode = Enum.TryParse<ExportMode>(GetString(data, "default_export_mode"), true, out var mode) ? mode : ExportMode.Compact,
            Indexing = GetSection(data, "indexing") is { } indexing ? new IndexingOptions
            {
                Enabled = GetBool(indexing, "enabled") ?? true,
                RefreshOnWrite = GetBool(indexing, "refresh_on_write") ?? false,
            } : new IndexingOptions(),
            Retrieval = GetSection(data, "retrieval") is { } retrieval ? new RetrievalOptions
            {
                DiagnosticTopK = GetInt(retrieval, "diagnostic_top_k") ?? 10,
                AnswerTopK = GetInt(retrieval, "answer_top_k") ?? 3,
                MaxSnippetLines = GetInt(retrieval, "max_snippet_lines") ?? 4,
            } : new RetrievalOptions(),
            Metadata = GetSection(data, "metadata") is { } metadata ? new MetadataOptions
            {
                FrontMatter = GetBool(metadata, "front_matter") ?? true,
            } : new MetadataOptions(),
            Handoffs = GetSection(data, "handoffs") is { } handoffs ? new HandoffOptions
            {
                Directory = GetString(handoffs, "directory") ?? "handoffs",
            } : new HandoffOptions(),
            Validation = GetSection(data, "validation") is { } validation ? new ValidationOptions
            {
                RequireIndex = GetBool(validation, "require_index") ?? true,
                RequireState = GetBool(validation, "require_state") ?? true,
            } : new ValidationOptions(),
        };
    }

    public string GetStorageRoot(string repositoryRoot) => Path.Combine(repositoryRoot, ".fractal-memory");

    private static IReadOnlyDictionary<object, object?>? GetSection(IReadOnlyDictionary<object, object?> source, string key) =>
        source.TryGetValue(key, out var section) ? section as IReadOnlyDictionary<object, object?> : null;

    private static string? GetString(IReadOnlyDictionary<object, object?> source, string key) =>
        source.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool? GetBool(IReadOnlyDictionary<object, object?> source, string key) =>
        source.TryGetValue(key, out var value) && bool.TryParse(value?.ToString(), out var parsed) ? parsed : null;

    private static int? GetInt(IReadOnlyDictionary<object, object?> source, string key) =>
        source.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var parsed) ? parsed : null;
}

public sealed class NodeService(
    IRepositoryService repositoryService,
    IFileSystemService fileSystemService,
    IMarkdownFileService markdownFileService,
    ITemplateService templateService) : INodeService
{
    public string NormalizeNodePath(string inputPath) => NodePathRules.Normalize(inputPath);

    public async Task<MemoryNode> CreateNodeAsync(string workingDirectory, string nodePath, CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");

        var normalizedPath = NormalizeNodePath(nodePath);
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var fullPath = Path.Combine(storageRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        if (fileSystemService.DirectoryExists(fullPath))
        {
            throw new InvalidOperationException($"Node '{normalizedPath}' already exists.");
        }

        fileSystemService.CreateDirectory(fullPath);
        fileSystemService.CreateDirectory(Path.Combine(fullPath, "children"));
        fileSystemService.CreateDirectory(Path.Combine(fullPath, "artifacts"));

        var templates = await templateService.GetNodeTemplatesAsync(repositoryRoot, Path.GetFileName(normalizedPath), cancellationToken);
        foreach (var template in templates)
        {
            await fileSystemService.WriteAllTextAsync(Path.Combine(fullPath, template.Key), template.Value + Environment.NewLine, cancellationToken);
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

    public async Task<IReadOnlyList<MemoryNode>> GetAllNodesAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var directories = fileSystemService.EnumerateFiles(storageRoot, "index.md", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(path => fileSystemService.FileExists(Path.Combine(path, "state.md")))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}templates{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}archive{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var nodes = new List<MemoryNode>(directories.Length);
        foreach (var directory in directories)
        {
            var relativePath = Path.GetRelativePath(storageRoot, directory).Replace(Path.DirectorySeparatorChar, '/');
            nodes.Add(await LoadNodeAsync(repositoryRoot, relativePath, cancellationToken));
        }

        return nodes.OrderBy(node => node.RelativePath, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<MemoryNode>> GetChildNodesAsync(string repositoryRoot, MemoryNode node, CancellationToken cancellationToken)
    {
        var childrenDirectory = Path.Combine(node.FullPath, "children");
        var children = new List<MemoryNode>();
        foreach (var childDirectory in fileSystemService.EnumerateDirectories(childrenDirectory))
        {
            if (!fileSystemService.FileExists(Path.Combine(childDirectory, "index.md")) ||
                !fileSystemService.FileExists(Path.Combine(childDirectory, "state.md")))
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
        var fullPath = Path.Combine(storageRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        if (!fileSystemService.DirectoryExists(fullPath))
        {
            throw new InvalidOperationException($"Node '{normalizedPath}' does not exist.");
        }

        var index = await markdownFileService.ReadAsync(Path.Combine(fullPath, "index.md"), cancellationToken);
        var state = await markdownFileService.ReadAsync(Path.Combine(fullPath, "state.md"), cancellationToken);
        var timelinePath = Path.Combine(fullPath, "timeline.md");
        var decisionsPath = Path.Combine(fullPath, "decisions.md");
        var timeline = fileSystemService.FileExists(timelinePath)
            ? await markdownFileService.ReadAsync(timelinePath, cancellationToken)
            : new ParsedMarkdownDocument { Metadata = new NodeMetadata(), Content = string.Empty };
        var decisions = fileSystemService.FileExists(decisionsPath)
            ? await markdownFileService.ReadAsync(decisionsPath, cancellationToken)
            : new ParsedMarkdownDocument { Metadata = new NodeMetadata(), Content = string.Empty };

        return new MemoryNode
        {
            RelativePath = normalizedPath,
            FullPath = fullPath,
            Metadata = MergeMetadata(index.Metadata, state.Metadata),
            IndexContent = index.Content,
            StateContent = state.Content,
            TimelineContent = timeline.Content,
            DecisionsContent = decisions.Content,
        };
    }

    private static NodeMetadata MergeMetadata(NodeMetadata primary, NodeMetadata secondary) =>
        new()
        {
            Title = primary.Title ?? secondary.Title,
            Aliases = primary.Aliases.Count > 0 ? primary.Aliases : secondary.Aliases,
            Tags = primary.Tags.Count > 0 ? primary.Tags : secondary.Tags,
            Status = primary.Status,
            Priority = primary.Priority,
            LastUpdated = primary.LastUpdated ?? secondary.LastUpdated,
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
        RetrievalDepth depth,
        NodeViewType view,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var node = await nodeService.GetNodeAsync(repositoryRoot, nodePath, cancellationToken);
        var children = await nodeService.GetChildNodesAsync(repositoryRoot, node, cancellationToken);
        var structuredState = structuredMemoryService.Parse(node.StateContent, node.RelativePath);

        var title = node.Metadata.Title ?? Path.GetFileName(node.RelativePath);
        var summary = node.Metadata.Summary
            ?? structuredState.CurrentObjective
            ?? ExtractSummary(node.IndexContent)
            ?? ExtractSummary(node.StateContent)
            ?? "No summary available.";

        return new OpenNodeResult
        {
            RelativePath = node.RelativePath,
            Title = title,
            Summary = summary,
            Depth = depth,
            View = view,
            IndexSummary = depth >= RetrievalDepth.Orientation && view is NodeViewType.Index or NodeViewType.Children
                ? TrimToParagraphs(node.IndexContent, depth == RetrievalDepth.Working ? 2 : 3)
                : null,
            CurrentState = depth >= RetrievalDepth.Working || view == NodeViewType.State
                ? TrimToParagraphs(node.StateContent, depth == RetrievalDepth.Deep ? 3 : 2)
                : null,
            Children = children.Select(child => $"{child.RelativePath} - {child.Metadata.Summary ?? ExtractSummary(child.IndexContent) ?? child.Metadata.Title ?? Path.GetFileName(child.RelativePath)}").ToArray(),
            SuggestedReads = BuildSuggestedReads(node, children),
            RecentTimeline = depth == RetrievalDepth.Deep || view == NodeViewType.Timeline
                ? ExtractHighlights(node.TimelineContent, 3)
                : [],
            RecentDecisions = depth == RetrievalDepth.Deep || view == NodeViewType.Decisions
                ? ExtractHighlights(node.DecisionsContent, 3)
                : [],
        };
    }

    private static IReadOnlyList<string> BuildSuggestedReads(MemoryNode node, IReadOnlyList<MemoryNode> children)
    {
        var suggestions = new List<string> { $"{node.RelativePath}/state.md" };
        if (!string.IsNullOrWhiteSpace(node.DecisionsContent))
        {
            suggestions.Add($"{node.RelativePath}/decisions.md");
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

    internal static IReadOnlyList<string> ExtractHighlights(string content, int maxItems) =>
        content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#'))
            .TakeLast(maxItems)
            .ToArray();
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
        ExportMode mode,
        CancellationToken cancellationToken)
    {
        var depth = mode switch
        {
            ExportMode.Compact => RetrievalDepth.Orientation,
            ExportMode.Standard => RetrievalDepth.Deep,
            ExportMode.Verbose => RetrievalDepth.Deep,
            _ => RetrievalDepth.Orientation,
        };

        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
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
            Mode = mode,
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
        var node = await nodeService.GetNodeAsync(repositoryRoot, nodePath, cancellationToken);
        var opened = await readService.OpenAsync(repositoryRoot, nodePath, RetrievalDepth.Deep, NodeViewType.Index, cancellationToken);
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
            $"{opened.RelativePath}/index.md",
            $"{opened.RelativePath}/state.md",
            $"{opened.RelativePath}/decisions.md",
        }).Distinct(StringComparer.Ordinal).ToArray();
        var sourceReferences = answerContext.SupportingSources
            .Select(source =>
            {
                var range = source.StartLine is not null && source.EndLine is not null ? $"#L{source.StartLine}-{source.EndLine}" : string.Empty;
                return $"- `{source.SourcePath}{range}`";
            })
            .ToArray();
        var freshness = node.Metadata.LastUpdated?.ToString("O", CultureInfo.InvariantCulture) ?? "Unknown";

        var timestamp = clock.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var safeName = opened.RelativePath.Replace('/', '-');
        var handoffRelativePath = Path.Combine(".fractal-memory", config.Handoffs.Directory, $"{timestamp}-{safeName}.md");
        var fullPath = Path.Combine(repositoryRoot, handoffRelativePath);

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

        await fileSystemService.WriteAllTextAsync(fullPath, content, cancellationToken);

        return new HandoffPacket
        {
            RelativePath = opened.RelativePath,
            HandoffFilePath = handoffRelativePath.Replace(Path.DirectorySeparatorChar, '/'),
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
    INodeService nodeService,
    IFileSystemService fileSystemService,
    IMarkdownFileService markdownFileService) : IValidationService
{
    public async Task<ValidationReport> ValidateAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var repositoryRoot = repositoryService.FindRepositoryRoot(workingDirectory)
            ?? throw new InvalidOperationException("No FractalMemory repository found.");
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var issues = new List<ValidationIssue>();
        var storageRoot = repositoryService.GetStorageRoot(repositoryRoot);
        var candidateDirectories = fileSystemService.EnumerateFiles(storageRoot, "*.md", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(path =>
                fileSystemService.FileExists(Path.Combine(path, "index.md")) ||
                fileSystemService.FileExists(Path.Combine(path, "state.md")))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}templates{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var nodes = await nodeService.GetAllNodesAsync(repositoryRoot, cancellationToken);
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

            var indexPath = Path.Combine(directory, "index.md");
            var statePath = Path.Combine(directory, "state.md");
            if (config.Validation.RequireIndex && !fileSystemService.FileExists(indexPath))
            {
                issues.Add(new ValidationIssue { Severity = ValidationSeverity.Error, RelativePath = relativePath, Message = "Missing required index.md." });
            }

            if (config.Validation.RequireState && !fileSystemService.FileExists(statePath))
            {
                issues.Add(new ValidationIssue { Severity = ValidationSeverity.Error, RelativePath = relativePath, Message = "Missing required state.md." });
            }

            foreach (var markdownFile in new[] { indexPath, statePath, Path.Combine(directory, "timeline.md"), Path.Combine(directory, "decisions.md") })
            {
                if (!fileSystemService.FileExists(markdownFile))
                {
                    continue;
                }

                try
                {
                    var document = await markdownFileService.ReadAsync(markdownFile, cancellationToken);
                    if (Path.GetFileName(markdownFile).Equals("state.md", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(document.Content))
                    {
                        issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, RelativePath = relativePath, Message = "state.md is empty." });
                    }

                    if (string.IsNullOrWhiteSpace(document.Metadata.Title))
                    {
                        issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, RelativePath = relativePath, Message = $"{Path.GetFileName(markdownFile)} is missing a title." });
                    }

                    if (document.Metadata.LastUpdated is null)
                    {
                        issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, RelativePath = relativePath, Message = $"{Path.GetFileName(markdownFile)} is missing last_updated." });
                    }
                }
                catch (Exception exception)
                {
                    issues.Add(new ValidationIssue { Severity = ValidationSeverity.Error, RelativePath = relativePath, Message = $"{Path.GetFileName(markdownFile)} has malformed front matter: {exception.Message}" });
                }
            }

            if (fileSystemService.FileExists(indexPath))
            {
                try
                {
                    var indexDocument = await markdownFileService.ReadAsync(indexPath, cancellationToken);
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

        var duplicates = nodes.GroupBy(node => node.RelativePath.ToLowerInvariant())
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

        var latestNodeWrite = fileSystemService.EnumerateFiles(storageRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}templates{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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
                Message = "Indexes may be stale.",
            });
        }

        return new ValidationReport { Issues = issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.RelativePath, StringComparer.Ordinal).ToArray() };
    }
}
