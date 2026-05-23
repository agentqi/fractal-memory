# Handoff: Fractal Memory CLI

Current goal: finish the FractalMemoryBench benchmark harness.

Current state:

- benchmark harness scaffold is in progress
- MCP remains optional
- exports must include CSV, JSON, and Markdown

Recent decisions:

- keep adapters modular
- keep the harness deterministic and local-first

Open questions:

- whether to align optional adapters with MemoryBench when installed

Next best actions:

- finish scoring
- finish reporting
- add regression tests
