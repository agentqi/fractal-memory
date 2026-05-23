---
description: Export a FractalMem node as AI-friendly structured text
argument-hint: <node-path>
allowed-tools:
  - mcp__fractalmem__memory_export
---

Export the FractalMem node `$ARGUMENTS`.

Call `memory_export` with standard mode unless the user asks for compact or verbose. Use the result as grounded context and preserve source labels in the response.
