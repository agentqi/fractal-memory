using System.Globalization;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;

namespace FractalMemory.Core.Infrastructure.Files;

public sealed class TemplateService(IFileSystemService fileSystemService, IClock clock) : ITemplateService
{
    public IReadOnlyDictionary<string, string> GetRepositoryTemplates()
    {
        var templates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["root/index.md"] = """
                ---
                title: Root Memory
                status: active
                priority: high
                last_updated: {{LAST_UPDATED}}
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
                last_updated: {{LAST_UPDATED}}
                summary: Current working state for the overall repository.
                ---

                # Current State

                ## Project / Branch

                root

                ## Current Objective

                <!-- fractalmem-placeholder: Capture the current operating reality, active priorities, and known constraints here. -->

                ## Active Constraints

                <!-- fractalmem-placeholder: Add the active constraints that apply right now. -->

                ## Next Best Actions

                <!-- fractalmem-placeholder: Add the next best actions to move the repository forward. -->

                ## Open Questions

                <!-- fractalmem-placeholder: Add unresolved questions that block progress. -->
                """,
            ["root/timeline.md"] = """
                ---
                title: Root Timeline
                status: active
                priority: medium
                last_updated: {{LAST_UPDATED}}
                summary: Chronological updates for the repository.
                ---

                # Timeline

                <!-- fractalmem-placeholder: Add dated updates here. -->
                """,
            ["root/decisions.md"] = """
                ---
                title: Root Decisions
                status: active
                priority: medium
                last_updated: {{LAST_UPDATED}}
                summary: Important cross-cutting decisions and rationale.
                ---

                # Decisions

                <!-- fractalmem-placeholder: Record key decisions and why they were made. -->
                """,
            ["templates/node/index.md"] = """
                ---
                title: {{TITLE}}
                status: active
                priority: medium
                last_updated: {{LAST_UPDATED}}
                summary: Summary for {{TITLE}}.
                ---

                # {{TITLE}}

                ## Summary

                <!-- fractalmem-placeholder: Describe what this node is for. -->

                ## Navigation

                - State: `state.md`
                - Timeline: `timeline.md`
                - Decisions: `decisions.md`
                """,
            ["templates/node/state.md"] = """
                ---
                title: {{TITLE}} State
                status: active
                priority: medium
                last_updated: {{LAST_UPDATED}}
                summary: Current working truth for {{TITLE}}.
                ---

                # Current State

                ## Project / Branch

                {{TITLE}}

                ## Current Objective

                <!-- fractalmem-placeholder: Capture what is currently true, active, and important. -->

                ## Active Constraints

                <!-- fractalmem-placeholder: Add constraints, guardrails, or decisions in force. -->

                ## Next Best Actions

                <!-- fractalmem-placeholder: Add the next best actions. -->

                ## Open Questions

                <!-- fractalmem-placeholder: Add unresolved questions. -->
                """,
            ["templates/node/timeline.md"] = """
                ---
                title: {{TITLE}} Timeline
                status: active
                priority: medium
                last_updated: {{LAST_UPDATED}}
                summary: Chronological updates for {{TITLE}}.
                ---

                # Timeline

                <!-- fractalmem-placeholder: Add dated entries here. -->
                """,
            ["templates/node/decisions.md"] = """
                ---
                title: {{TITLE}} Decisions
                status: active
                priority: medium
                last_updated: {{LAST_UPDATED}}
                summary: Important decisions for {{TITLE}}.
                ---

                # Decisions

                <!-- fractalmem-placeholder: Record decisions and rationale here. -->
                """,
        };

        var timestamp = FormatTimestamp();
        return templates.ToDictionary(
            pair => pair.Key,
            pair => pair.Key.StartsWith("root/", StringComparison.Ordinal)
                ? pair.Value.Replace("{{LAST_UPDATED}}", timestamp, StringComparison.Ordinal)
                : pair.Value,
            StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string> GetNodeTemplates(string nodeName, NodeFileFormat format = NodeFileFormat.Markdown)
    {
        var title = Humanize(nodeName);
        var timestamp = FormatTimestamp();
        if (format == NodeFileFormat.Html)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["index.html"] = $$"""
                    <!doctype html>
                    <html lang="en">
                    <head>
                      <meta charset="utf-8">
                      <title>{{title}}</title>
                      <meta name="summary" content="Summary for {{title}}.">
                    </head>
                    <body>
                      <h1>{{title}}</h1>
                      <section>
                        <h2>Summary</h2>
                        <p><!-- fractalmem-placeholder: Describe the role of this node and when it should be loaded. --></p>
                      </section>
                      <section>
                        <h2>Suggested Reads</h2>
                        <ul>
                          <li><code>state.html</code></li>
                          <li><code>timeline.html</code></li>
                          <li><code>decisions.html</code></li>
                        </ul>
                      </section>
                    </body>
                    </html>
                    """,
                ["state.html"] = $$"""
                    <!doctype html>
                    <html lang="en">
                    <head>
                      <meta charset="utf-8">
                      <title>{{title}} State</title>
                      <meta name="summary" content="Current working truth for {{title}}.">
                    </head>
                    <body>
                      <h1>Current State</h1>
                      <section>
                        <h2>Project / Branch</h2>
                        <p>{{title}}</p>
                      </section>
                      <section>
                        <h2>Current Objective</h2>
                        <p><!-- fractalmem-placeholder: Capture what is true right now. --></p>
                      </section>
                      <section>
                        <h2>Active Constraints</h2>
                        <ul><li><!-- fractalmem-placeholder: Add constraints, guardrails, or decisions in force. --></li></ul>
                      </section>
                      <section>
                        <h2>Next Best Actions</h2>
                        <ul><li><!-- fractalmem-placeholder: Add the next best actions. --></li></ul>
                      </section>
                      <section>
                        <h2>Open Questions</h2>
                        <ul><li><!-- fractalmem-placeholder: Add unresolved questions. --></li></ul>
                      </section>
                    </body>
                    </html>
                    """,
                ["timeline.html"] = $$"""
                    <!doctype html>
                    <html lang="en">
                    <head>
                      <meta charset="utf-8">
                      <title>{{title}} Timeline</title>
                      <meta name="summary" content="Chronological updates for {{title}}.">
                    </head>
                    <body>
                      <h1>Timeline</h1>
                      <ul><li><!-- fractalmem-placeholder: Add dated updates here. --></li></ul>
                    </body>
                    </html>
                    """,
                ["decisions.html"] = $$"""
                    <!doctype html>
                    <html lang="en">
                    <head>
                      <meta charset="utf-8">
                      <title>{{title}} Decisions</title>
                      <meta name="summary" content="Important decisions for {{title}}.">
                    </head>
                    <body>
                      <h1>Decisions</h1>
                      <ul><li><!-- fractalmem-placeholder: Record decisions and rationale here. --></li></ul>
                    </body>
                    </html>
                    """,
            };
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["index.md"] = $$"""
                ---
                title: {{title}}
                status: active
                priority: medium
                last_updated: {{timestamp}}
                summary: Summary for {{title}}.
                ---

                # {{title}}

                ## Summary

                <!-- fractalmem-placeholder: Describe the role of this node and when it should be loaded. -->

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
                last_updated: {{timestamp}}
                summary: Current working truth for {{title}}.
                ---

                # Current State

                ## Project / Branch

                {{title}}

                ## Current Objective

                <!-- fractalmem-placeholder: Capture what is true right now. -->

                ## Active Constraints

                <!-- fractalmem-placeholder: Add constraints, guardrails, or decisions in force. -->

                ## Next Best Actions

                <!-- fractalmem-placeholder: Add the next best actions. -->

                ## Open Questions

                <!-- fractalmem-placeholder: Add unresolved questions. -->
                """,
            ["timeline.md"] = $$"""
                ---
                title: {{title}} Timeline
                status: active
                priority: medium
                last_updated: {{timestamp}}
                summary: Chronological updates for {{title}}.
                ---

                # Timeline

                <!-- fractalmem-placeholder: Add dated updates here. -->
                """,
            ["decisions.md"] = $$"""
                ---
                title: {{title}} Decisions
                status: active
                priority: medium
                last_updated: {{timestamp}}
                summary: Important decisions for {{title}}.
                ---

                # Decisions

                <!-- fractalmem-placeholder: Record decisions and rationale here. -->
                """,
        };
    }

    public async Task<IReadOnlyDictionary<string, string>> GetNodeTemplatesAsync(
        string repositoryRoot,
        string nodeName,
        NodeFileFormat format,
        bool includeFrontMatter,
        CancellationToken cancellationToken)
    {
        var storageRoot = RepositoryPathGuard.ResolveContainedPath(repositoryRoot, ".fractal-memory");
        var templateRoot = RepositoryPathGuard.ResolveContainedPath(storageRoot, "templates/node");
        if (format == NodeFileFormat.Html)
        {
            return GetNodeTemplates(nodeName, format);
        }

        var templateFiles = new[] { "index.md", "state.md", "timeline.md", "decisions.md" };
        if (!templateFiles.All(file => fileSystemService.FileExists(Path.Combine(templateRoot, file))))
        {
            return ApplyFrontMatterPreference(GetNodeTemplates(nodeName, format), includeFrontMatter);
        }

        var title = Humanize(nodeName);
        var timestamp = FormatTimestamp();
        var templates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in templateFiles)
        {
            var path = RepositoryPathGuard.ResolveContainedPath(templateRoot, file);
            var content = await fileSystemService.ReadAllTextAsync(path, cancellationToken);
            templates[file] = content
                .Replace("{{TITLE}}", title, StringComparison.Ordinal)
                .Replace("{{LAST_UPDATED}}", timestamp, StringComparison.Ordinal);
        }

        return ApplyFrontMatterPreference(templates, includeFrontMatter);
    }

    private static IReadOnlyDictionary<string, string> ApplyFrontMatterPreference(
        IReadOnlyDictionary<string, string> templates,
        bool includeFrontMatter)
    {
        if (includeFrontMatter)
        {
            return templates;
        }

        return templates.ToDictionary(
            pair => pair.Key,
            pair => StripFrontMatter(pair.Value),
            StringComparer.Ordinal);
    }

    private static string StripFrontMatter(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return content;
        }

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var closing = normalized.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        return closing < 0 ? content : normalized[(closing + 5)..].TrimStart('\n');
    }

    private string FormatTimestamp() => clock.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static string Humanize(string value) =>
        string.Join(' ', value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
}
