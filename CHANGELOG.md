# Changelog

## Unreleased

- Added hash-checked section updates, dated timeline entries and managed decisions with supersession across CLI and MCP.
- Added full document/section/artifact reads with source hashes, source coordinates and native MCP resource links.
- Added review scheduling, stale/incomplete memory attention lists and explicit archived-node discovery.
- Added bounded context packs and node-scoped resumption with handoff source-hash comparisons.
- Added preview-first single-note import with source preservation, duplicate/conflict detection and atomic node staging.
- Added doctor/index repair, CLI JSON output and actionable MCP validation errors.
- Shared source-aware heading parsing across workflows and retrieval; ignored legacy scaffold guidance as recorded knowledge and upgraded derived caches to format 3.
- Preserved HTML inline word boundaries and excluded staging/archive/artifact trees consistently from discovery, recent activity and validation.
- Existing repositories: older templates wrote `indexing.refresh_on_write: false`, which was previously ignored. Set it to `true` for automatic refresh, then run `fm index refresh`; explicit `false` remains respected. Missing settings now default to `true`, and validation explains this migration when indexes are stale.
- Search/recent scopes use canonical node segments (lowercase letters, digits and hyphens); `./projects`, underscores and dotted segments are rejected. Rename noncanonical manual directories reported by validation before using them as scopes. Search limits must be positive; omit the limit to use the configured default.

- Fixed metadata-only cache invalidation; explicit refresh now bypasses caches, verifies cache integrity, repairs damaged files, and upgrades older cache formats.
- Made deep and focused state reads complete, selected populated sections for working views, and exposed an abridged-state indicator.
- Preserved original Markdown source line numbers through parsing and caching, and restored state evidence in exports and handoffs.
- Preserved HTML headings and lists for structured memory; HTML search cites actual filenames and omits inaccurate generated line coordinates.
- Fixed one-line snippets dropping valid matches and isolated scoped searches from malformed neighboring nodes.
- Staged and validated nodes before publication, cleaned up interrupted creation, and added validation for incomplete scaffolds.
- Rejected undefined numeric enum arguments and export configuration values across CLI and shared services.
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
