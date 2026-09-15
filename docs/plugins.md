# Plugins

FractalMem integrates with agent tools through the MCP server.

Install the MCP server from NuGet after a release is published:

```bash
dotnet tool install -g FractalMemory.McpServer
```

Then configure clients to run:

```bash
fractalmem-mcp
```

Set `FRACTALMEM_REPOSITORY_ROOT` when the client launches outside the repository that contains `.fractal-memory/`.

## Codex

The Codex plugin source is in `plugins/codex-fractalmem/`.

It contributes:

- MCP server config
- FractalMem usage skill
- marketplace entry in `.agents/plugins/marketplace.json`

## Claude Code

The Claude Code plugin source is in `plugins/claude-fractalmem/`.

It contributes:

- MCP server config
- FractalMem usage skill
- slash commands for opening, searching, recent activity, exporting, handoff creation, validation, and index refresh
- marketplace entry in `.claude-plugin/marketplace.json`

## Local package verification

Build and exercise the same package path used by CI:

```bash
dotnet restore FractalMemory.sln
dotnet build FractalMemory.sln --configuration Release --no-restore -warnaserror
dotnet pack FractalMemory.sln --configuration Release --no-build --output artifacts
version=$(sed -n 's/.*<VersionPrefix>\([^<]*\)<.*/\1/p' Directory.Build.props)
bash scripts/smoke-packages.sh artifacts "$version"
```

The smoke test installs both tools into an isolated directory, exercises the CLI, performs a real MCP initialize/list/call exchange, and verifies a clean repository validation result.
