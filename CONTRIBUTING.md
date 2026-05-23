# Contributing

Thanks for improving FractalMem.

## Development

Requirements:

- .NET SDK 10.0.100 or later
- Python 3.11+ only for the external benchmark repository

Build:

```bash
dotnet build FractalMemory.sln
```

Test:

```bash
dotnet test FractalMemory.sln
```

## Guidelines

- Keep core behavior local-first and filesystem-backed.
- Keep CLI and MCP wrappers thin over `FractalMemory.Core`.
- Add focused tests for changes to search, validation, indexing, export, or repository layout.
- Do not commit generated outputs, local memory stores, package caches, or benchmark run artifacts.

## Release Checklist

- update `CHANGELOG.md`
- run `dotnet test FractalMemory.sln`
- pack CLI and MCP server tools
- test Codex and Claude plugin manifests against local installs
