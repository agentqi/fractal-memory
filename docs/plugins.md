# Plugins

FractalMem integrates with agent tools through the MCP server.

Install the MCP server as a .NET tool once it is published:

```bash
dotnet tool install -g FractalMemory.McpServer
```

Then configure clients to run:

```bash
fractalmem-mcp
```

Set `FRACTALMEM_REPOSITORY_ROOT` when the client launches outside the repository that contains `.fractal-memory/`.

## Codex

The Codex plugin skeleton is in `plugins/codex-fractalmem/`.

It contributes:

- MCP server config
- FractalMem usage skill
- marketplace entry in `.agents/plugins/marketplace.json`

## Claude Code

The Claude Code plugin skeleton is in `plugins/claude-fractalmem/`.

It contributes:

- MCP server config
- FractalMem usage skill
- slash commands for opening, searching, recent activity, exporting, handoff creation, validation, and index refresh
- marketplace entry in `.claude-plugin/marketplace.json`
