---
name: fractalmem
description: Use FractalMem when the user wants durable local project memory, context retrieval, decision lookup, recent work summaries, handoff creation, or a narrow memory export through the FractalMem MCP tools.
---

# FractalMem

Use the `fractalmem` MCP tools as the source of truth for local structured memory.

## Workflow

1. For orientation, call `memory_open` with `depth: 1`.
2. For active implementation context, call `memory_open` with `depth: 2`.
3. For recent movement, call `memory_recent` scoped to the relevant node path.
4. For exact lookups, call `memory_search` before reading broader context.
5. For prompt-ready context, call `memory_export`.
6. Before pausing or handing work to another agent, call `memory_handoff_create`.

Keep memory reads narrow. Prefer the most specific node path available, such as `projects/<name>`, `systems/<name>`, or `research/<topic>`.

## Repository Setup

If no FractalMem repository exists, ask to run:

```bash
fm init
```

Then create a node:

```bash
fm node create projects/<project-name>
```

The MCP server expects `fractalmem-mcp` on PATH and resolves the repository from `FRACTALMEM_REPOSITORY_ROOT` or the current workspace.
