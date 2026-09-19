# Try FractalMem locally

**Project memory you can inspect, update, and resume.** This source-checkout path is intended for the initial pilot. It does not assume packages have been published to NuGet.

## 1. Install this checkout

You need the .NET 10 SDK and Python 3.11+ for the installer/demo. From the FractalMem repository:

```bash
python3 scripts/install-local.py
python3 scripts/demo.py
```

On Windows use `py -3` instead of `python3`. The scripts also accept absolute paths, including paths with spaces.

The installer builds the CLI and MCP server in Release mode, then installs those exact packages into `.tools/fractalmem/`. It leaves your global tools and agent settings alone. It requires a new destination; for another build use `--tool-dir /absolute/path/to/new-tools` and pass its `fm` executable to the demo with `--cli`.

The demo uses a temporary project and verifies capture, fresh-process resume, decision supersession and stale-write rejection. To inspect its files afterward:

```bash
python3 scripts/demo.py --workspace /absolute/path/to/new-demo-folder
```

Build time is separate from demo execution. Measure your own first successful resume; “five minutes” is a pilot target, not a measured user onboarding promise.

## 2. Use a real project

Run the installed executable from the project directory you want to give memory. Substitute its actual absolute path; use `fm.exe` on Windows.

```bash
/absolute/path/to/fractalmem/.tools/fractalmem/fm init
/absolute/path/to/fractalmem/.tools/fractalmem/fm node create projects/my-project
/absolute/path/to/fractalmem/.tools/fractalmem/fm read projects/my-project --json
```

Record one objective, one constraint, one current decision and one next action. Use the document hash from `read` when writing, so an older agent session cannot silently replace a newer update. See [memory workflows](memory-workflows.md) for exact update/append examples.

Before ending a session, create a handoff. In a fresh session, resume the same node:

```bash
/absolute/path/to/fractalmem/.tools/fractalmem/fm handoff create projects/my-project
/absolute/path/to/fractalmem/.tools/fractalmem/fm resume projects/my-project --json
```

## 3. Connect an agent over MCP

Use your client's stdio MCP settings to launch the installed server and point it at the project root (the directory containing `.fractal-memory`, not `.fractal-memory` itself). A common JSON shape is:

```json
{
  "mcpServers": {
    "fractalmem": {
      "command": "/absolute/path/to/fractalmem/.tools/fractalmem/fractalmem-mcp",
      "env": {
        "FRACTALMEM_REPOSITORY_ROOT": "/absolute/path/to/your/project"
      }
    }
  }
}
```

Client configuration formats differ; this is not an installer for every client. Use `fractalmem-mcp.exe` and escaped backslashes on Windows. [Plugin instructions](plugins.md) cover the bundled Codex and Claude Code integrations.

Suggested first agent request:

> Resume projects/my-project with FractalMem. Read the current objective, constraints and active decisions. Cite the source for the next action. Say what is missing. Before writing, read the current document hash and preserve unrelated sections.

## Storage and privacy

Memory is stored locally as readable Markdown/HTML. A connected agent may send retrieved content to its model provider under that client's settings. Local storage does not guarantee an offline AI session. FractalMem has no hosted sync service or automatic background fact capture in this pilot. Record only facts you intend to retain and share with that agent.

The context character budget covers `context.text`, including source labels. JSON metadata, duplicate excerpts and MCP resource links add transport tokens. See the [precise budget contract](memory-workflows.md#bounded-context-and-full-sources).

## If something fails

- `dotnet --info`: confirm .NET 10 SDK is available.
- “Tool directory already exists”: choose a new `--tool-dir`; existing tools are preserved.
- Repository not found: run from your project or set `FRACTALMEM_REPOSITORY_ROOT` for MCP.
- Stale hash: read again, reconcile the newer content, then write with the new hash.
- Missing or malformed memory: run `fm doctor`; use `fm doctor --repair` to rebuild derived indexes after source errors are corrected.

To remove a pilot installation, delete only its chosen tool directory. Your project memory is separate and remains until you choose to remove it.
