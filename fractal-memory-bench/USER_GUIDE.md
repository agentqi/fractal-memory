# FractalMemoryBench User Guide

This guide explains how to install, configure, run, and interpret `FractalMemoryBench`.

## What The Harness Does

`FractalMemoryBench` compares different memory approaches against the same:

- corpus
- task definitions
- reset simulation
- scoring logic

It supports two benchmark layers:

- retrieval benchmarking
- end-to-end LLM benchmarking

The harness is local-first by default. You can run it fully offline with the bundled corpus and the deterministic mock LLM provider.

## Repository Layout

Important paths:

- [README.md](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/README.md)
- [benchmark_runner/configs](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/configs)
- [benchmark_runner/tasks](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/tasks)
- [benchmark_runner/corpus](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/corpus)
- [benchmark_runner/outputs](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/outputs)

## Installation

From the benchmark repo:

```bash
cd /Users/telli/Desktop/fm\ cli/fractal-memory-bench
python3.11 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

Run the test suite:

```bash
pytest
```

## Benchmark Modes

The harness supports three execution modes in config:

- `retrieval_only`
- `end_to_end_llm`
- `hybrid`

### `retrieval_only`

Measures retrieval quality only:

- files opened
- lines loaded
- token estimate
- precision
- recall
- branch purity
- adjusted retrieval score

### `end_to_end_llm`

Runs retrieval, then builds a stable prompt, calls an LLM provider, and scores the final answer.

Additional metrics include:

- LLM input/output/total tokens
- latency
- cost estimate
- final answer score
- contamination penalties
- hallucinated continuity penalties
- task completion score

### `hybrid`

Captures both retrieval and end-to-end answer metrics in one run.

## Core Concepts

### Corpus

The corpus is the durable memory source under [benchmark_runner/corpus](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/corpus).

### Tasks

Each YAML file under [benchmark_runner/tasks](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/tasks) defines:

- task prompt
- target project
- must-recover phrases
- distractors
- expected paths
- answer rubric

### Adapters

Each approach under test implements the same adapter interface. Current adapters include:

- `no_persistence`
- `single_file`
- `flat_folders`
- `fractal_cli`
- `auto_retrieval_stub`
- `memorybench_adapter`
- `mcp_adapter_stub`

### Reset Simulation

If enabled, the harness:

1. sends a partial prompt
2. clears volatile adapter state
3. reruns retrieval using only durable memory

This simulates continuity after reset or context compaction.

## Quick Start

### 1. Validate Corpus

```bash
python3.11 -m benchmark_runner.cli validate-corpus
```

### 2. List Tasks

```bash
python3.11 -m benchmark_runner.cli list-tasks
```

### 3. Run Retrieval-Only Pilot

```bash
python3.11 -m benchmark_runner.cli run --approach all --config benchmark_runner/configs/pilot.yaml
```

### 4. Run Mock LLM Pilot

```bash
python3.11 -m benchmark_runner.cli run --approach flat_folders --config benchmark_runner/configs/pilot_llm.yaml
```

### 5. Re-Score Existing Results

```bash
python3.11 -m benchmark_runner.cli score benchmark_runner/outputs/latest/results.json
```

### 6. Generate a Report

```bash
python3.11 -m benchmark_runner.cli report benchmark_runner/outputs/latest/results.csv
```

### 7. Inspect a Prompt Before Running

```bash
python3.11 -m benchmark_runner.cli dry-run-prompt --approach flat_folders --task A1 --config benchmark_runner/configs/pilot_llm.yaml
```

## Config Files

Available bundled configs:

- [default.yaml](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/configs/default.yaml)
- [pilot.yaml](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/configs/pilot.yaml)
- [full.yaml](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/configs/full.yaml)
- [pilot_llm.yaml](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/configs/pilot_llm.yaml)
- [full_llm.yaml](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/configs/full_llm.yaml)
- [pilot_openai.yaml](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/configs/pilot_openai.yaml)
- [full_openai.yaml](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/configs/full_openai.yaml)

Important config fields:

- `mode`
- `approaches`
- `task_ids`
- `repetitions`
- `simulate_reset`
- `randomize_task_order`
- `output_directory`
- `corpus_directory`
- `tasks_directory`
- `max_files_per_retrieval`
- `fractal_cli_use_live_cli`
- `fractal_cli_command`
- `llm.*`
- `providers.*`
- `answer_scoring.*`

## Using The Real Fractal Memory CLI

The `fractal_cli` adapter supports live executable-backed benchmarking.

When `fractal_cli_use_live_cli: true` is set:

1. the harness discovers the adjacent `.NET` CLI project
2. builds it locally with `dotnet build`
3. creates a temporary `.fractal-memory` workspace
4. hydrates that workspace from the benchmark corpus
5. runs real CLI commands such as:
   - `init`
   - `index refresh`
   - `open`
   - `export`
   - `handoff create`

This path uses the real executable, but still stays fully local.

## LLM Providers

### Default Safe Mode: `mock`

The mock provider is deterministic and uses no network.

Use it for:

- local development
- tests
- CI
- prompt debugging

### Real Providers

Supported provider abstractions:

- `openai`
- `openai_compatible`
- `anthropic` placeholder

These require config and environment variables before use.

Examples:

- `OPENAI_API_KEY`
- `OPENAI_BASE_URL`

Real provider calls are only made if you choose a non-mock provider in config.

### Simplest OpenAI Setup

The easiest real-provider path is:

```bash
export OPENAI_API_KEY=your_key_here
python3.11 -m benchmark_runner.cli run --approach fractal_cli --config benchmark_runner/configs/pilot_openai.yaml
```

Those bundled configs already set:

- `provider: openai`
- `api_key_env: OPENAI_API_KEY`
- `model: gpt-5.4`

So in the normal case you only need to add the key.

## Outputs

Each run writes to a timestamped directory under [benchmark_runner/outputs](/Users/telli/Desktop/fm%20cli/fractal-memory-bench/benchmark_runner/outputs).

Typical files:

- `results.json`
- `results.csv`
- `report.md`

`latest/` is refreshed to point at the newest run output.

### JSON

Best for full-fidelity inspection:

- retrieval result
- LLM request
- LLM response
- answer score
- adapter metadata

### CSV

Best for aggregation and spreadsheet analysis.

### Markdown Report

Best for quick human review:

- retrieval summary
- end-to-end LLM summary
- comparison tables
- top failures
- task-family breakdown

## How Scoring Works

### Retrieval Layer

Retrieval scoring uses:

- project/branch identification
- current objective recovery
- decision recovery
- active constraint recovery
- next-action recovery

Penalties apply for:

- irrelevant context overload
- contamination
- wrong continuity claims

### Answer Layer

In LLM modes, answer scoring adds:

- final answer score
- contamination penalty
- hallucinated continuity penalty
- unsupported claim penalty
- task completion score

The first implementation is rule-based for reproducibility.

## Recommended Workflows

### Fast Local Iteration

Use:

- `pilot.yaml`
- `pilot_llm.yaml`
- `mock` provider
- a single approach

### Real CLI Comparison

Use:

- `pilot.yaml` or `full.yaml`
- `fractal_cli_use_live_cli: true`
- `flat_folders` as a contrast baseline

### Prompt Debugging

Use:

- `dry-run-prompt`
- `mock` provider

### Full Study

Use:

- `full_llm.yaml`
- multiple approaches
- several repetitions

## Troubleshooting

### `python: command not found`

Use `python3.11` explicitly.

### Real CLI run fails

Check:

- `.NET 10` SDK is installed
- [FractalMemory.Cli.csproj](/Users/telli/Desktop/fm%20cli/src/FractalMemory.Cli/FractalMemory.Cli.csproj) exists
- the workspace builds with `dotnet build`

### Real provider fails

Check:

- provider name in config
- required environment variables
- base URL for compatible endpoints

### Benchmark output looks too noisy

Check:

- `max_files_per_retrieval`
- task selection
- adapter behavior
- `dry-run-prompt` output

## Suggested Starting Commands

Retrieval-only:

```bash
python3.11 -m benchmark_runner.cli run --approach all --config benchmark_runner/configs/pilot.yaml
```

Mock LLM:

```bash
python3.11 -m benchmark_runner.cli run --approach flat_folders --config benchmark_runner/configs/pilot_llm.yaml
```

Live Fractal CLI:

```bash
python3.11 -m benchmark_runner.cli run --approach fractal_cli --config benchmark_runner/configs/pilot.yaml
```

Prompt inspection:

```bash
python3.11 -m benchmark_runner.cli dry-run-prompt --approach fractal_cli --task C1 --config benchmark_runner/configs/pilot_llm.yaml
```
