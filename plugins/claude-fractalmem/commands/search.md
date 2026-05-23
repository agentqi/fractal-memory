---
description: Search FractalMem by content, title, alias, or tag
argument-hint: <query>
allowed-tools:
  - mcp__fractalmem__memory_search
---

Search FractalMem for `$ARGUMENTS`.

Call `memory_search` and summarize the highest-signal matches. If the user names a node prefix (for example "in projects/" or "scoped to research/topic"), pass it as `scope` to narrow the search. Preserve source paths, section headings, and line ranges when returned.
