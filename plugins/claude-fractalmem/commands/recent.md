---
description: Show recent FractalMem activity to resume work
argument-hint: [scope]
allowed-tools:
  - mcp__fractalmem__memory_recent
---

Show recent FractalMem activity for `$ARGUMENTS`.

Call `memory_recent`. If `$ARGUMENTS` looks like a node path prefix (for example `projects/` or `research/topic`), pass it as the `scope` parameter; otherwise leave `scope` unset. Default to 30 days and 10 items unless the user asks otherwise. Summarize each result with the node path, file, and last-modified timestamp.
