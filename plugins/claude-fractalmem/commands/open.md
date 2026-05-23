---
description: Open a FractalMem node with layered retrieval depth
argument-hint: <node-path>
allowed-tools:
  - mcp__fractalmem__memory_open
---

Open the FractalMem node `$ARGUMENTS`.

Call `memory_open` with depth 1 first. If the user asks for implementation state, call it again with depth 2. Summarize the result with source paths and suggested next reads.
