using System.Text;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Output;

public sealed class HumanFormatter : IHumanFormatter
{
    public string FormatInitialization(string repositoryRoot) =>
        $"Initialized FractalMem repository at {Path.Combine(repositoryRoot, ".fractal-memory")}";

    public string FormatNodeCreated(MemoryNode node) =>
        $"Created node {node.RelativePath}";

    public string FormatOpen(OpenNodeResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{result.Title} ({result.RelativePath})");
        builder.AppendLine(result.Summary);

        if (!string.IsNullOrWhiteSpace(result.IndexSummary))
        {
            builder.AppendLine();
            builder.AppendLine("Index");
            builder.AppendLine(result.IndexSummary);
        }

        if (!string.IsNullOrWhiteSpace(result.CurrentState))
        {
            builder.AppendLine();
            builder.AppendLine("State");
            builder.AppendLine(result.CurrentState);
        }

        if (result.Children.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Children");
            foreach (var child in result.Children)
            {
                builder.AppendLine($"- {child}");
            }
        }

        if (result.SuggestedReads.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Suggested Reads");
            foreach (var read in result.SuggestedReads)
            {
                builder.AppendLine($"- {read}");
            }
        }

        if (result.RecentTimeline.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recent Timeline");
            foreach (var item in result.RecentTimeline)
            {
                builder.AppendLine($"- {item}");
            }
        }

        if (result.RecentDecisions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recent Decisions");
            foreach (var item in result.RecentDecisions)
            {
                builder.AppendLine($"- {item}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public string FormatSearch(IReadOnlyList<SearchResult> results, string query)
    {
        if (results.Count == 0)
        {
            return $"No matches found for \"{query}\".";
        }

        var builder = new StringBuilder();
        foreach (var result in results)
        {
            builder.AppendLine($"{result.RelativePath} [{result.Score}]");
            builder.AppendLine($"  title: {result.Title}");
            builder.AppendLine($"  file: {result.MatchedFile}");
            builder.AppendLine($"  source: {result.SourcePath}");
            if (!string.IsNullOrWhiteSpace(result.SectionHeading))
            {
                builder.AppendLine($"  section: {result.SectionHeading}");
            }

            if (result.StartLine is not null && result.EndLine is not null)
            {
                builder.AppendLine($"  lines: {result.StartLine}-{result.EndLine}");
            }

            builder.AppendLine($"  snippet: {result.Snippet}");
        }

        return builder.ToString().TrimEnd();
    }

    public string FormatRecent(IReadOnlyList<RecentItem> items)
    {
        if (items.Count == 0)
        {
            return "No recent items found.";
        }

        var builder = new StringBuilder();
        foreach (var item in items)
        {
            builder.AppendLine($"{item.LastModified:yyyy-MM-dd HH:mm}Z  {item.RelativePath}  ({item.FileName})");
        }

        return builder.ToString().TrimEnd();
    }

    public string FormatValidation(ValidationReport report)
    {
        if (report.Issues.Count == 0)
        {
            return "Validation passed with no issues.";
        }

        var builder = new StringBuilder();
        foreach (var issue in report.Issues)
        {
            builder.AppendLine($"{issue.Severity}: {issue.RelativePath} - {issue.Message}");
        }

        return builder.ToString().TrimEnd();
    }

    public string FormatHandoff(HandoffPacket packet) =>
        $"Created handoff {packet.HandoffFilePath}";

    public string FormatIndexesRefreshed() => "Indexes refreshed.";
}

public sealed class AiExportFormatter : IAiExportFormatter
{
    public string Format(ExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[memory:{document.RelativePath}]");
        builder.AppendLine($"title: {document.Title}");
        builder.AppendLine($"mode: {document.Mode.ToString().ToLowerInvariant()}");
        builder.AppendLine($"source_path: {document.RelativePath}");
        builder.AppendLine();
        builder.AppendLine("summary:");
        builder.AppendLine(document.Summary);

        if (!string.IsNullOrWhiteSpace(document.CurrentState))
        {
            builder.AppendLine();
            builder.AppendLine("current_state:");
            builder.AppendLine(document.CurrentState);
        }

        if (document.AnswerContext is not null)
        {
            builder.AppendLine();
            builder.AppendLine("answer_context:");
            builder.AppendLine($"  project_branch: {document.AnswerContext.ProjectBranch}");
            if (!string.IsNullOrWhiteSpace(document.AnswerContext.CurrentObjective))
            {
                builder.AppendLine("  current_objective:");
                builder.AppendLine($"    {document.AnswerContext.CurrentObjective}");
            }

            if (document.AnswerContext.KeyPriorDecisions.Count > 0)
            {
                builder.AppendLine("  key_prior_decisions:");
                foreach (var decision in document.AnswerContext.KeyPriorDecisions)
                {
                    builder.AppendLine($"    - {decision}");
                }
            }

            if (document.AnswerContext.ActiveConstraints.Count > 0)
            {
                builder.AppendLine("  active_constraints:");
                foreach (var constraint in document.AnswerContext.ActiveConstraints)
                {
                    builder.AppendLine($"    - {constraint}");
                }
            }

            if (document.AnswerContext.NextBestActions.Count > 0)
            {
                builder.AppendLine("  next_best_actions:");
                foreach (var action in document.AnswerContext.NextBestActions)
                {
                    builder.AppendLine($"    - {action}");
                }
            }

            if (document.AnswerContext.MissingInformation.Count > 0)
            {
                builder.AppendLine("  missing_information:");
                foreach (var item in document.AnswerContext.MissingInformation)
                {
                    builder.AppendLine($"    - {item}");
                }
            }

            if (document.AnswerContext.SupportingSources.Count > 0)
            {
                builder.AppendLine("  supporting_sources:");
                foreach (var source in document.AnswerContext.SupportingSources)
                {
                    var range = source.StartLine is not null && source.EndLine is not null
                        ? $"#L{source.StartLine}-{source.EndLine}"
                        : string.Empty;
                    builder.AppendLine($"    - {source.SourcePath}{range}");
                    builder.AppendLine($"      {source.Excerpt.Replace(Environment.NewLine, " ", StringComparison.Ordinal)}");
                }
            }
        }

        if (document.Children.Count > 0 && document.Mode != Domain.Enums.ExportMode.Compact)
        {
            builder.AppendLine();
            builder.AppendLine("children:");
            foreach (var child in document.Children)
            {
                builder.AppendLine($"- {child}");
            }
        }

        if (document.TimelineHighlights.Count > 0 && document.Mode == Domain.Enums.ExportMode.Verbose)
        {
            builder.AppendLine();
            builder.AppendLine("timeline_highlights:");
            foreach (var item in document.TimelineHighlights)
            {
                builder.AppendLine($"- {item}");
            }
        }

        if (document.DecisionHighlights.Count > 0 && document.Mode != Domain.Enums.ExportMode.Compact)
        {
            builder.AppendLine();
            builder.AppendLine("decision_highlights:");
            foreach (var item in document.DecisionHighlights)
            {
                builder.AppendLine($"- {item}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
