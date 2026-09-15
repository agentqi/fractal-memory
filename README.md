# FractalMem

FractalMem is a local-first structured memory system for AI-assisted work. It stores durable project memory as markdown files in a fractal hierarchy so humans and agents can load only the relevant branch instead of dragging full history into every context.

FractalMem is designed to run as:

- a local `fm` CLI
- a stdio MCP server for agent clients
- a Codex plugin
- a Claude Code plugin

The solution is split into:

- `FractalMemory.Core`: domain, services, filesystem logic, indexing, search, export, validation, and formatting
- `FractalMemory.Cli`: `fm` command-line interface over the core services
- `FractalMemory.McpServer`: optional MCP server wrapper over the same core services

## Concept

Each memory node behaves like a chapter:

- `index.md` or `index.html`: front door, summary, navigation
- `state.md` or `state.html`: current working truth
- `timeline.md` or `timeline.html`: chronological updates
- `decisions.md` or `decisions.html`: important decisions and rationale
- `children/`: nested subnodes
- `artifacts/`: supporting markdown and HTML files

Files are the source of truth. Index files are accelerators, not authorities.

## Solution Layout

```text
FractalMemory.sln

src/
  FractalMemory.Core/
  FractalMemory.Cli/
  FractalMemory.McpServer/

tests/
  FractalMemory.Core.Tests/
  FractalMemory.Cli.Tests/

plugins/
  codex-fractalmem/
  claude-fractalmem/
```

## Build

Requirements:

- .NET SDK 10.0.100 or later

Build everything:

```bash
dotnet build FractalMemory.sln
```

Run all tests:

```bash
dotnet test --solution FractalMemory.sln
```

## Run The CLI

Run from source:

```bash
dotnet run --project src/FractalMemory.Cli -- init
```

Build the binary and run `fm` directly:

```bash
dotnet build src/FractalMemory.Cli
./src/FractalMemory.Cli/bin/Debug/net10.0/fm init
```

Pack local .NET tools:

```bash
dotnet pack src/FractalMemory.Cli
dotnet pack src/FractalMemory.McpServer
```

Install from a local package output:

```bash
dotnet tool install -g FractalMemory.Cli --add-source ./src/FractalMemory.Cli/bin/Release
dotnet tool install -g FractalMemory.McpServer --add-source ./src/FractalMemory.McpServer/bin/Release
```

## Example CLI Commands

- `fm init`
- `fm node create projects/fractal-memory-cli`
- `fm node create projects/design-notes --format html`
- `fm open projects/fractal-memory-cli`
- `fm open projects/fractal-memory-cli --depth 2`
- `fm search "context bloat"`
- `fm export projects/fractal-memory-cli --mode compact`
- `fm handoff create projects/fractal-memory-cli`
- `fm recent`
- `fm index refresh`
- `fm validate`

## Agent Plugins

FractalMem exposes the same core operations through a stdio MCP server:

```bash
fractalmem-mcp
```

The MCP server resolves the repository from its current working directory, or from `FRACTALMEM_REPOSITORY_ROOT` when an agent client launches it from another directory.

Installable plugin sources live under:

- `plugins/codex-fractalmem/`
- `plugins/claude-fractalmem/`

Marketplace metadata lives under:

- `.agents/plugins/marketplace.json`
- `.claude-plugin/marketplace.json`

See `docs/plugins.md` for plugin packaging and local installation notes.

## Repository Layout

`fm init` creates:

```text
.fractal-memory/
  config.yaml
  root/
    index.md
    state.md
    timeline.md
    decisions.md
    children/
    artifacts/
  projects/
  people/
  systems/
  research/
  handoffs/
  indexes/
    aliases.yaml
    tags.yaml
    paths.yaml
  templates/
    node/
      index.md
      state.md
      timeline.md
      decisions.md
  archive/
```

Each created node contains:

```text
<node>/
  index.md      # or index.html when created with --format html
  state.md      # or state.html
  timeline.md   # or timeline.html
  decisions.md  # or decisions.html
  children/
  artifacts/
```

## CLI Behavior Summary

- `init` creates the repository, config, templates, root files, and index stubs.
- `node create` normalizes and validates the path, creates the standard node contract as markdown or HTML, and refreshes indexes when both indexing and `refresh_on_write` are enabled.
- `open` supports layered retrieval with depth `0..3` and optional focused views.
- `search` ranks exact path, title, alias, tag, then markdown content matches, with optional path scoping.
- `export` emits AI-friendly structured output with stable source labels.
- `handoff create` writes resumable handoff markdown into the configured handoff directory (by default `.fractal-memory/handoffs/`).
- `recent` surfaces recently modified nodes for work resumption.
- `index refresh` rebuilds aliases, tags, and paths from filesystem truth.
- `validate` reports hard errors and warnings for structure, metadata, and stale indexes.

## Run The Optional MCP Server

The MCP server is optional. The CLI does not depend on it.

Run the stdio MCP server:

```bash
dotnet run --project src/FractalMemory.McpServer
```

By default, the server resolves repositories from its current working directory by walking upward for `.fractal-memory/config.yaml`, just like the CLI.

If the MCP host launches the server outside the repository, set `FRACTALMEM_REPOSITORY_ROOT` to the repository root:

```bash
FRACTALMEM_REPOSITORY_ROOT=/absolute/path/to/workspace \
dotnet run --project src/FractalMemory.McpServer
```

## Configuration

`.fractal-memory/config.yaml` controls retrieval, indexing, metadata, handoffs, and validation:

- `default_depth` is used by CLI and MCP `open` calls when no depth is supplied.
- `default_export_mode` is used by CLI and MCP exports when no mode is supplied.
- `indexing.enabled` enables indexes; `indexing.refresh_on_write` controls automatic refresh after node creation.
- `metadata.front_matter` controls whether new Markdown nodes include YAML front matter.
- `handoffs.directory` selects a relative directory inside `.fractal-memory/`.
- retrieval limits must be positive integers.

Configured storage directories cannot be absolute or contain `.` or `..` traversal. Symbolic links and reparse points inside `.fractal-memory/` are rejected so CLI and MCP operations cannot escape the repository boundary.

## MCP Surface

Tools:

- `memory_open(path, depth?, view?)`
- `memory_search(query, limit?, scope?)`
- `memory_recent(days?, limit?, scope?)`
- `memory_export(path, mode?)`
- `memory_handoff_create(path)`
- `memory_index_refresh()`
- `memory_validate()`

Resources:

- `memory://root/index`
- `memory://node/index/{path}`
- `memory://node/state/{path}`
- `memory://handoffs/latest`

For dynamic node resources, pass the repository-relative node path URL-encoded in `{path}`, for example `projects%2Ffractal-memory-cli`.

Prompts:

- `resume_project`
- `summarize_node`
- `create_handoff_from_node`

## Conceptual MCP Usage

Typical agent workflow:

1. Call `memory_open("projects/fractal-memory-cli", depth: 1)` for orientation.
2. Call `memory_recent(scope: "projects/fractal-memory-cli")` to inspect recent movement.
3. Call `memory_export("projects/fractal-memory-cli", mode: "standard")` when prompt-ready structured context is needed.
4. Call `memory_handoff_create("projects/fractal-memory-cli")` before handing work to another agent or later session.

## Notes

- Core logic is independent of both CLI and MCP.
- YAML front matter parsing is isolated behind `IFrontMatterParser`.
- The implementation is built for standard .NET 10 JIT execution first, while keeping the architecture relatively AOT-friendly.
- There is no database, network storage, telemetry, GUI, or cloud dependency in the core MVP.
- Benchmarking lives separately at https://github.com/agentqi/fractal-memory-bench.
- Release and branch-protection setup is documented in `docs/releasing.md`.
