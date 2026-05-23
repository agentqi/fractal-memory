# FractalMemoryBench

FractalMemoryBench is a local-first benchmark harness for comparing structured memory approaches on continuity, context efficiency, branch-selective retrieval, contamination resistance, and handoff quality.

For a step-by-step operator guide, see [USER_GUIDE.md](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/USER_GUIDE.md).

It is designed as a harness, not a product feature. The same benchmark tasks and corpus can be executed against multiple approaches behind a common adapter interface.

## Benchmark Tracks

- `Fractal Continuity Benchmark`: the built-in 12-task benchmark covering resume-after-reset, branch-selective retrieval, handoff/resume, and cross-project separation.
- Optional external adapters: shells for MemoryBench and future MCP-style evaluation.
- `LLM-in-the-loop mode`: an optional second execution layer that measures final answer quality after retrieval.

## Supported Approaches

- `no_persistence`
- `single_file`
- `flat_folders`
- `fractal_cli`
- `auto_retrieval_stub`
- `memorybench_adapter`
- `mcp_adapter_stub`

## Repository Layout

```text
fractal-memory-bench/
  benchmark_runner/
    adapters/
    configs/
    corpus/
    outputs/
    tasks/
  tests/
```

## Install

Requirements:

- Python 3.11+

Create a virtual environment and install dependencies:

```bash
cd /Users/telli/Desktop/fm\ cli/fractal-memory-bench
python3.11 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

Or install in editable mode:

```bash
pip install -e .
```

## Run

List tasks:

```bash
python -m benchmark_runner.cli list-tasks
```

Validate the bundled corpus:

```bash
python -m benchmark_runner.cli validate-corpus
```

Run the pilot benchmark:

```bash
python -m benchmark_runner.cli run --approach fractal_cli --config benchmark_runner/configs/pilot.yaml
```

Run the pilot LLM benchmark with the deterministic mock provider:

```bash
python3.11 -m benchmark_runner.cli run --approach flat_folders --config benchmark_runner/configs/pilot_llm.yaml
```

Run the pilot LLM benchmark with a real OpenAI model after setting your key:

```bash
export OPENAI_API_KEY=your_key_here
python3.11 -m benchmark_runner.cli run --approach fractal_cli --config benchmark_runner/configs/pilot_openai.yaml
```

Run all configured approaches:

```bash
python -m benchmark_runner.cli run --approach all --config benchmark_runner/configs/pilot.yaml
```

Re-score an existing JSON result file:

```bash
python -m benchmark_runner.cli score benchmark_runner/outputs/latest/results.json
```

Generate a Markdown summary from CSV or JSON:

```bash
python -m benchmark_runner.cli report benchmark_runner/outputs/latest/results.csv
```

Inspect the exact prompt that would be sent to the model:

```bash
python3.11 -m benchmark_runner.cli dry-run-prompt --approach flat_folders --task A1 --config benchmark_runner/configs/pilot_llm.yaml
```

## Pilot vs Full

- `benchmark_runner/configs/pilot.yaml` runs a smaller deterministic slice for fast local iteration.
- `benchmark_runner/configs/full.yaml` runs the full task set across all main approaches.
- `benchmark_runner/configs/pilot_llm.yaml` runs the pilot slice in `end_to_end_llm` mode.
- `benchmark_runner/configs/full_llm.yaml` runs the broader suite in `hybrid` mode.
- `benchmark_runner/configs/pilot_openai.yaml` is OpenAI-ready and only requires `OPENAI_API_KEY`.
- `benchmark_runner/configs/full_openai.yaml` is the larger OpenAI-ready run.

## Corpus

The bundled corpus is a realistic markdown memory tree with:

- `root/`
- `projects/fractal-memory-cli/`
- `projects/govos/`
- `projects/flowone/`
- `systems/architecture/`
- `people/ibrahim/`
- `archive/old-workflow-lab/`
- `archive/memory-research-notes/`
- `handoffs/`

## Fractal CLI Plug-In

The `fractal_cli` adapter works in two modes:

- default simulated structured retrieval over the bundled corpus
- optional shell mode if you later point it at a real Fractal Memory CLI command in config

When `fractal_cli_use_live_cli: true` is set, the adapter auto-discovers the adjacent `.NET` CLI project, builds it, initializes a real `.fractal-memory` workspace for the run, hydrates it from the benchmark corpus, refreshes indexes, and then retrieves through the live executable.

This keeps the benchmark locally runnable even without a live CLI binary, while still allowing real executable-backed evaluation when the .NET workspace is present.

## LLM-In-The-Loop Mode

The harness supports three execution modes:

- `retrieval_only`
- `end_to_end_llm`
- `hybrid`

In `end_to_end_llm` and `hybrid`, the flow is:

1. retrieval through the selected memory adapter
2. fixed prompt construction
3. model generation through an LLM provider
4. rule-based answer scoring

The default safe path is the deterministic `mock` provider, which makes tests and local runs reproducible without network access.

The repository also includes provider abstractions for:

- `mock`
- `openai`
- `openai_compatible`
- `anthropic` placeholder

By design, no real API calls happen unless you explicitly configure a real provider in the LLM config and set the required environment variables.

The bundled OpenAI-ready configs use `provider: openai` and expect `OPENAI_API_KEY`. The default model is `gpt-5.4`, which OpenAI documents for the Responses API in its official model guide and changelog: [GPT-5.4 guide](https://developers.openai.com/api/docs/guides/latest-model) and [API changelog](https://developers.openai.com/api/docs/changelog).

## Reporting

Retrieval-only runs report:

- retrieval score
- token estimate
- branch purity
- precision and recall

LLM-enabled runs additionally report:

- final answer score
- answer precision and recall
- contamination penalties
- hallucinated continuity penalties
- task completion score
- provider/model token usage and latency

## Optional MemoryBench Integration

FractalMemoryBench now includes an optional subprocess-backed MemoryBench bridge under `benchmark_runner/standard_benchmarks/`.

This bridge does not replace the custom runner. It adds a second standards-track backend that can invoke MemoryBench benchmarks such as LoCoMo and LongMemEval, normalize their outputs, and write them under the same run directory.

Key design points:

- MemoryBench is optional.
- The main Fractal Continuity Benchmark remains the primary system.
- Integration is subprocess-first and loosely coupled.
- MemoryBench metrics stay separate from Fractal-specific metrics such as branch purity and handoff quality.
- If MemoryBench is unavailable and `skip_if_unavailable: true`, the run is skipped gracefully instead of crashing the harness.

Example config block:

```yaml
standard_benchmarks:
  enabled: true
  backend: memorybench
  memorybench:
    mode: subprocess
    executable: bun
    working_dir: ./vendor/memorybench
    timeout_seconds: 3600
    preserve_raw_outputs: true
    skip_if_unavailable: true
    environment:
      OPENAI_API_KEY: ${OPENAI_API_KEY}
    runs:
      - benchmark: locomo
        evaluator: gpt-4o
      - benchmark: longmemeval
        evaluator: gpt-4o
```

When enabled, the regular `run` command will also emit a standard-benchmark manifest:

```bash
python3.11 -m benchmark_runner.cli run --approach fractal_cli --config benchmark_runner/configs/default.yaml
```

Outputs are written to:

```text
benchmark_runner/outputs/run-<id>/standard_benchmarks/memorybench/<benchmark>/<approach>/
  stdout.log
  stderr.log
  provider_wrapper.json
  raw/
  normalized/
    normalized.json
    normalized.csv
```

The bridge currently supports:

- `locomo`
- `longmemeval`

What it compares:

- MemoryBench-native benchmark metrics, normalized into a stable row and aggregate schema

What it does not compare:

- branch purity
- reset penalties
- handoff quality

Those remain part of the custom Fractal Continuity Benchmark.

## Extending The Harness

To add a new adapter:

1. Implement `MemoryApproachAdapter` in `benchmark_runner/adapters/`.
2. Register it in `benchmark_runner/runner.py`.
3. Add it to a config file.

This design is intended to make future MCP-Bench-like or Mem2ActBench-like adapters straightforward to add without changing benchmark tasks, scoring, or reporting.

## Tests

Run the harness tests with:

```bash
pytest
```

The test suite now covers:

- task and corpus loading
- retrieval scoring
- reset simulation
- CSV export
- prompt building
- mock provider behavior
- rule-based answer scoring
- end-to-end LLM runner flow with the mock provider
