# Security

FractalMem is local-first. The core CLI and MCP server store memory as files under `.fractal-memory/` in the selected repository and do not require a hosted service.

## MCP Server Access

The MCP server resolves a repository root from `FRACTALMEM_REPOSITORY_ROOT` or the current working directory. It only reads and writes inside that repository's `.fractal-memory/` storage directory.

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

Path traversal protections are enforced for MCP resources. Searchable artifacts are read from node `artifacts/` directories only.
