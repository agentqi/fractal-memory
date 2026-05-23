---
name: fractalmem
description: Use FractalMem when the user wants durable local project memory, context retrieval, decision lookup, recent work summaries, handoff creation, or a narrow memory export through the FractalMem MCP tools.
---

# FractalMem

Use FractalMem MCP tools as the source of truth for local structured memory.

- Start with `memory_open` at depth 1 for orientation.
- Use depth 2 only when active state is needed.
- Use depth 3 only when timeline or decisions materially affect the task.
- Use `memory_search` for exact lookups before broad reads.
- Use `memory_export` when a compact, source-labeled packet is needed.
- Use `memory_handoff_create` before pausing or transferring work.

The MCP server expects `fractalmem-mcp` on PATH and resolves the repository from `FRACTALMEM_REPOSITORY_ROOT` or the Claude project directory.
