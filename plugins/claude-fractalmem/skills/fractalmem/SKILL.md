---
name: fractalmem
description: Use FractalMem when the user wants durable local project memory, context retrieval, decision lookup, recent work summaries, handoff creation, or a narrow memory export through the FractalMem MCP tools.
---

# FractalMem

Use FractalMem MCP tools as the source of truth for local structured memory.

- Start with `memory_open` at depth 1 for orientation.
- Use depth 2 only when active state is needed.
- Use depth 3 only when timeline or decisions materially affect the task.
- Use `memory_search` for exact lookups before broad reads. Pass `limit` when you need more than the default top-10.
- Use `memory_recent` to resume work or to inspect activity scoped to a node prefix (for example `projects/` or `research/topic`).
- Use `memory_export` when a compact, source-labeled packet is needed.
- Use `memory_handoff_create` before pausing or transferring work.
- Use `memory_validate` to surface metadata or structural issues, and `memory_index_refresh` after manual edits or when validation reports stale indexes.

Slash commands wrap each tool: `/memory open`, `/memory search`, `/memory recent`, `/memory export`, `/memory handoff`, `/memory validate`, `/memory refresh`.

The MCP server expects `fractalmem-mcp` on PATH and resolves the repository from `FRACTALMEM_REPOSITORY_ROOT` or the Claude project directory.
