# Security

FractalMem is local-first. The core CLI and MCP server store memory as files under `.fractal-memory/` in the selected repository and do not require a hosted service.

## MCP Server Access

The MCP server resolves a repository root from `FRACTALMEM_REPOSITORY_ROOT` or the current working directory. It only reads and writes inside that repository's `.fractal-memory/` storage directory. Absolute paths, relative traversal, symbolic links, and filesystem reparse points are rejected.

The server exposes tools for opening memory nodes, searching indexed content, exporting node context, creating handoff files, refreshing indexes, and validating repository structure.

## Reporting Issues

Report security issues privately to the maintainers before public disclosure. Include:

- affected version or commit
- operating system
- reproduction steps
- expected and actual behavior
- whether the issue allows reading or writing outside `.fractal-memory/`

## Threat Model

FractalMem treats local markdown and supported artifact files as trusted project data. Do not put secrets in memory files unless you are comfortable exposing them to local agents that can call the MCP server.

Path containment protections are shared by the CLI, core services, MCP tools, MCP resources, handoff writer, index cache, and searchable artifacts. FractalMem intentionally does not follow symbolic links inside `.fractal-memory/`; copy trusted content into the memory tree instead.

Generated writes use same-directory temporary files followed by atomic replacement. This prevents readers from observing partially written config, memory, or index files, but callers should still serialize competing semantic updates to the same file.
