# Changelog

## Unreleased

- Enforced `.fractal-memory/` containment for reads, writes, handoffs, resources, indexes, and caches; symbolic links and reparse points are rejected.
- Made generated writes atomic and handoff filenames collision-resistant.
- Fixed generated node titles and added `last_updated` values so fresh repositories validate cleanly.
- Honored configured CLI/MCP defaults, `refresh_on_write`, `metadata.front_matter`, and custom handoff directories; removed the unused `root_dir` setting.
- Added cross-platform CI, CodeQL, dependency auditing, package/MCP smoke testing, and tag-driven NuGet/GitHub release automation.
- Updated .NET, command-line, YAML, MCP, coverage, and test-platform dependencies.
- Replaced machine-specific documentation paths and documented configuration, security, plugin verification, and release operations.

## 0.1.0

- Initial FractalMem CLI, core library, and MCP server.
- Local `.fractal-memory/` repository layout with nodes, handoffs, indexes, templates, and validation.
- Codex and Claude Code plugin packaging sources.
