using FractalMemory.Core.Application.Services;

namespace FractalMemory.Core.Infrastructure.Files;

public sealed class TemplateService(IFileSystemService fileSystemService) : ITemplateService
{
    public IReadOnlyDictionary<string, string> GetRepositoryTemplates() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["root/index.md"] = """
                ---
                title: Root Memory
                status: active
                priority: high
                summary: Entry point for the repository memory tree.
                ---

                # Root Memory

                Use this node as the top-level orientation page for the repository.

                ## Focus Areas

                - Projects live under `projects/`
                - People live under `people/`
                - Systems live under `systems/`
                - Research notes live under `research/`
                """,
            ["root/state.md"] = """
                ---
                title: Root State
                status: active
                priority: high
                summary: Current working state for the overall repository.
                ---

                # Current State

                ## Project / Branch

                root

                ## Current Objective

                Capture the current operating reality, active priorities, and known constraints here.

                ## Active Constraints

                - Add the active constraints that apply right now.

                ## Next Best Actions

                - Add the next best actions to move the repository forward.

                ## Open Questions

                - Add unresolved questions that block progress.
                """,
            ["root/timeline.md"] = """
                ---
                title: Root Timeline
                status: active
                priority: medium
                summary: Chronological updates for the repository.
                ---

                # Timeline

                - Add dated updates here.
                """,
            ["root/decisions.md"] = """
                ---
                title: Root Decisions
                status: active
                priority: medium
                summary: Important cross-cutting decisions and rationale.
                ---

                # Decisions

                - Record key decisions and why they were made.
                """,
            ["templates/node/index.md"] = """
                ---
                title: Node Template Index
                status: active
                priority: medium
                summary: Template for node summary and navigation.
                ---

                # {{TITLE}}

                ## Summary

                Describe what this node is for.

                ## Navigation

                - State: `state.md`
                - Timeline: `timeline.md`
                - Decisions: `decisions.md`
                """,
            ["templates/node/state.md"] = """
                ---
                title: Node Template State
                status: active
                priority: medium
                summary: Template for the current working truth.
                ---

                # Current State

                ## Project / Branch

                {{TITLE}}

                ## Current Objective

                Capture what is currently true, active, and important.

                ## Active Constraints

                - Add constraints, guardrails, or decisions in force.

                ## Next Best Actions

                - Add the next best actions.

                ## Open Questions

                - Add unresolved questions.
                """,
            ["templates/node/timeline.md"] = """
                ---
                title: Node Template Timeline
                status: active
                priority: medium
                summary: Template for chronological updates.
                ---

                # Timeline

                - Add dated entries here.
                """,
            ["templates/node/decisions.md"] = """
                ---
                title: Node Template Decisions
                status: active
                priority: medium
                summary: Template for important decisions.
                ---

                # Decisions

                - Record decisions and rationale here.
                """,
        };

    public IReadOnlyDictionary<string, string> GetNodeTemplates(string nodeName)
    {
        var title = Humanize(nodeName);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["index.md"] = $$"""
                ---
                title: {{title}}
                status: active
                priority: medium
                summary: Summary for {{title}}.
                ---

                # {{title}}

                ## Summary

                Describe the role of this node and when it should be loaded.

                ## Suggested Reads

                - `state.md`
                - `timeline.md`
                - `decisions.md`
                """,
            ["state.md"] = $$"""
                ---
                title: {{title}} State
                status: active
                priority: medium
                summary: Current working truth for {{title}}.
                ---

                # Current State

                ## Project / Branch

                {{title}}

                ## Current Objective

                Capture what is true right now.

                ## Active Constraints

                - Add constraints, guardrails, or decisions in force.

                ## Next Best Actions

                - Add the next best actions.

                ## Open Questions

                - Add unresolved questions.
                """,
            ["timeline.md"] = $$"""
                ---
                title: {{title}} Timeline
                status: active
                priority: medium
                summary: Chronological updates for {{title}}.
                ---

                # Timeline

                - Add dated updates here.
                """,
            ["decisions.md"] = $$"""
                ---
                title: {{title}} Decisions
                status: active
                priority: medium
                summary: Important decisions for {{title}}.
                ---

                # Decisions

                - Record decisions and rationale here.
                """,
        };
    }

    public async Task<IReadOnlyDictionary<string, string>> GetNodeTemplatesAsync(
        string repositoryRoot,
        string nodeName,
        CancellationToken cancellationToken)
    {
        var templateRoot = Path.Combine(repositoryRoot, ".fractal-memory", "templates", "node");
        var templateFiles = new[] { "index.md", "state.md", "timeline.md", "decisions.md" };
        if (!templateFiles.All(file => fileSystemService.FileExists(Path.Combine(templateRoot, file))))
        {
            return GetNodeTemplates(nodeName);
        }

        var title = Humanize(nodeName);
        var templates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in templateFiles)
        {
            var path = Path.Combine(templateRoot, file);
            var content = await fileSystemService.ReadAllTextAsync(path, cancellationToken);
            templates[file] = content.Replace("{{TITLE}}", title, StringComparison.Ordinal);
        }

        return templates;
    }

    private static string Humanize(string value) =>
        string.Join(' ', value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
}
