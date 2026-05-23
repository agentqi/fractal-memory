using System.Text.RegularExpressions;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Infrastructure.Parsing;

public sealed partial class StructuredMemoryService : IStructuredMemoryService
{
    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"[^a-z0-9/ ]")]
    private static partial Regex NormalizeHeadingRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static readonly Dictionary<string, string> HeadingMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["project / branch"] = "project_branch",
        ["project branch"] = "project_branch",
        ["current objective"] = "current_objective",
        ["current goal"] = "current_objective",
        ["key decisions in force"] = "key_decisions",
        ["key prior decision"] = "key_decisions",
        ["key prior decisions"] = "key_decisions",
        ["active constraints"] = "active_constraints",
        ["constraint"] = "active_constraints",
        ["next best actions"] = "next_best_actions",
        ["next action"] = "next_best_actions",
        ["open questions"] = "open_questions",
        ["missing information"] = "open_questions",
        ["last updated"] = "freshness",
        ["freshness"] = "freshness",
        ["source references"] = "source_references",
        ["supporting sources"] = "source_references",
    };

    public StructuredMemoryFields Parse(string markdown, string fallbackProjectBranch)
    {
        var sections = ParseSections(markdown);
        var fields = new StructuredMemoryFields
        {
            ProjectBranch = FirstValue(sections, "project_branch") ?? fallbackProjectBranch,
            CurrentObjective = FirstValue(sections, "current_objective") ?? FallbackObjective(markdown),
            KeyDecisionsInForce = ListValue(sections, "key_decisions"),
            ActiveConstraints = ListValue(sections, "active_constraints").Count > 0
                ? ListValue(sections, "active_constraints")
                : ExtractConstraintLines(markdown),
            NextBestActions = ListValue(sections, "next_best_actions"),
            OpenQuestions = ListValue(sections, "open_questions").Count > 0
                ? ListValue(sections, "open_questions")
                : ExtractQuestionLines(markdown),
            Freshness = FirstValue(sections, "freshness"),
            SourceReferences = ListValue(sections, "source_references"),
        };

        return fields;
    }

    public AnswerContextPacket BuildAnswerContext(
        MemoryNode node,
        IReadOnlyList<SearchResult> diagnosticResults,
        int answerTopK,
        int diagnosticTopK)
    {
        var parsedState = Parse(node.StateContent, node.RelativePath);
        var parsedDecisions = Parse(node.DecisionsContent, node.RelativePath);

        var supportingSources = diagnosticResults
            .Take(answerTopK)
            .Select(result => new SupportingSource
            {
                SourcePath = result.SourcePath,
                SectionHeading = result.SectionHeading,
                StartLine = result.StartLine,
                EndLine = result.EndLine,
                Excerpt = result.Snippet,
            })
            .DistinctBy(source => $"{source.SourcePath}|{source.SectionHeading}|{source.Excerpt}")
            .ToArray();

        var keyDecisions = parsedState.KeyDecisionsInForce.Count > 0
            ? parsedState.KeyDecisionsInForce
            : parsedDecisions.KeyDecisionsInForce.Count > 0
                ? parsedDecisions.KeyDecisionsInForce
                : ExtractBulletLines(node.DecisionsContent, 4);

        var activeConstraints = parsedState.ActiveConstraints.Count > 0
            ? parsedState.ActiveConstraints
            : ExtractConstraintLines(node.StateContent);

        var nextActions = parsedState.NextBestActions.Count > 0
            ? parsedState.NextBestActions
            : [];

        var missingInformation = new List<string>();
        if (string.IsNullOrWhiteSpace(parsedState.CurrentObjective))
        {
            missingInformation.Add("Current Objective");
        }

        if (keyDecisions.Count == 0)
        {
            missingInformation.Add("Key Prior Decision");
        }

        if (activeConstraints.Count == 0)
        {
            missingInformation.Add("Active Constraint");
        }

        if (nextActions.Count == 0)
        {
            missingInformation.Add("Next Best Action");
        }

        return new AnswerContextPacket
        {
            ProjectBranch = parsedState.ProjectBranch ?? node.RelativePath,
            CurrentObjective = parsedState.CurrentObjective ?? node.Metadata.Summary,
            KeyPriorDecisions = keyDecisions,
            ActiveConstraints = activeConstraints,
            NextBestActions = nextActions,
            MissingInformation = missingInformation,
            SupportingSources = supportingSources,
            DiagnosticResults = diagnosticResults.Take(diagnosticTopK).ToArray(),
        };
    }

    private static Dictionary<string, List<string>> ParseSections(string markdown)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        var currentLines = new List<string>();

        foreach (var line in NormalizeLineEndings(markdown).Split('\n'))
        {
            var headingMatch = HeadingRegex().Match(line);
            if (headingMatch.Success)
            {
                FlushSection(sections, currentKey, currentLines);
                currentKey = NormalizeHeading(headingMatch.Groups[1].Value);
                currentLines = new List<string>();
                continue;
            }

            if (currentKey is not null)
            {
                currentLines.Add(line);
            }
        }

        FlushSection(sections, currentKey, currentLines);
        return sections;
    }

    private static void FlushSection(Dictionary<string, List<string>> sections, string? key, List<string> lines)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var content = lines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (content.Count == 0)
        {
            return;
        }

        sections[key] = content;
    }

    private static string NormalizeHeading(string heading)
    {
        var normalized = NormalizeHeadingRegex().Replace(heading.ToLowerInvariant(), " ").Trim();
        normalized = WhitespaceRegex().Replace(normalized, " ");
        return HeadingMap.TryGetValue(normalized, out var mapped) ? mapped : normalized;
    }

    private static string NormalizeLineEndings(string markdown) =>
        markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string? FirstValue(IReadOnlyDictionary<string, List<string>> sections, string key) =>
        sections.TryGetValue(key, out var values)
            ? values.Select(CleanListPrefix).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            : null;

    private static IReadOnlyList<string> ListValue(IReadOnlyDictionary<string, List<string>> sections, string key) =>
        sections.TryGetValue(key, out var values)
            ? values.Select(CleanListPrefix).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            : [];

    private static string? FallbackObjective(string markdown) =>
        NormalizeLineEndings(markdown)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanListPrefix)
            .FirstOrDefault(line => !line.StartsWith('#') && !string.IsNullOrWhiteSpace(line));

    private static IReadOnlyList<string> ExtractQuestionLines(string markdown) =>
        NormalizeLineEndings(markdown)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanListPrefix)
            .Where(line => line.Contains('?', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> ExtractConstraintLines(string markdown) =>
        NormalizeLineEndings(markdown)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanListPrefix)
            .Where(line =>
                line.Contains("constraint", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("must ", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("cannot ", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();

    private static IReadOnlyList<string> ExtractBulletLines(string markdown, int maxItems) =>
        NormalizeLineEndings(markdown)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.TrimStart().StartsWith('-'))
            .Select(CleanListPrefix)
            .Take(maxItems)
            .ToArray();

    private static string CleanListPrefix(string line) =>
        line.Trim().TrimStart('-', '*', ' ');
}
