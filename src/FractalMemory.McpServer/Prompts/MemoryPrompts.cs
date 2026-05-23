using System.ComponentModel;
using ModelContextProtocol.Server;

namespace FractalMemory.McpServer.Prompts;

[McpServerPromptType]
public sealed class MemoryPrompts
{
    [McpServerPrompt(Name = "resume_project")]
    [Description("Guide an AI agent to resume work from a project memory node.")]
    public static string ResumeProject(
        [Description("Repository-relative node path to resume.")] string path) =>
        $"""
        Resume work from the FractalMem node `{path}`.
        Read in this order:
        1. `memory_open` with depth 1 for orientation.
        2. `memory_open` with depth 2 if active implementation context is needed.
        3. `memory_recent` scoped to `{path}` to inspect recent movement.
        4. `memory_handoff_create` if a fresh resumable summary is needed.
        Keep context narrow and follow the node's stated current truth and decisions.
        """;

    [McpServerPrompt(Name = "summarize_node")]
    [Description("Guide an AI agent to summarize a node without loading unnecessary history.")]
    public static string SummarizeNode(
        [Description("Repository-relative node path to summarize.")] string path) =>
        $"""
        Summarize the FractalMem node `{path}`.
        Start with `memory_open` depth 1.
        If the summary needs implementation detail, use `memory_open` depth 2.
        Only use depth 3 if recent timeline or decisions materially affect the answer.
        Preserve source path references in the final summary.
        """;

    [McpServerPrompt(Name = "create_handoff_from_node")]
    [Description("Guide an AI agent to produce and use a handoff from a node.")]
    public static string CreateHandoffFromNode(
        [Description("Repository-relative node path to hand off.")] string path) =>
        $"""
        Create a resumable handoff for `{path}` using `memory_handoff_create`.
        Then summarize:
        - the current goal
        - the current working state
        - recent decisions
        - open questions
        - next best actions
        Keep the handoff aligned with the memory files as the source of truth.
        """;
}
