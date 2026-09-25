# Benchmarks

The [benchmark repository](https://github.com/agentqi/fractal-memory-bench) contains the Python runner, development fixtures, adapters and an optional external benchmark bridge.

Use its [comparison protocol](https://github.com/agentqi/fractal-memory-bench/blob/main/docs/comparison-protocol.md) and [development evidence](https://github.com/agentqi/fractal-memory-bench/blob/main/docs/evidence/README.md). Links to new materials become available on the default branch after the benchmark PR merges.

The corrected harness keeps gold answers out of retrieval and answer prompts, uses real CLI responses, counts complete delivered context, and separates live runs from mocks and simulations. The 12 bundled tasks remain author-written diagnostics. They do not support a competitive ranking or claims of better model accuracy.

Compare both native response sizes and equal token budgets; report ingestion/setup, maintenance effort and task outcomes as well as retrieval. The product's `--max-characters` budget applies to source-linked context text, not the entire CLI/MCP response. See [the budget contract](memory-workflows.md#bounded-context-and-full-sources).

The vendored MemoryBench `fractalmemory` provider is a separate TypeScript implementation. Its results must not be advertised as measurements of this .NET application. Basic Memory, Mem0 and Graphiti/Zep require real integrations and separate evaluation before making comparative claims.
