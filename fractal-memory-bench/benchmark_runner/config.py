from __future__ import annotations

from pathlib import Path

import yaml
from pydantic import BaseModel, Field

from benchmark_runner.standard_benchmarks.datasets import normalize_benchmark_name
from benchmark_runner.standard_benchmarks.models import MemoryBenchRunConfig, StandardBenchmarksConfig


class LlmSettings(BaseModel):
    enabled: bool = False
    provider: str = "mock"
    model: str = "mock-model"
    temperature: float = 0.0
    max_tokens: int = 220
    timeout_seconds: int = 60
    save_full_prompts: bool = True
    save_full_responses: bool = True


class ProviderEndpointConfig(BaseModel):
    base_url_env: str | None = None
    api_key_env: str | None = None


class AnswerScoringConfig(BaseModel):
    enabled: bool = True
    use_llm_judge: bool = False
    use_rule_based_scorer: bool = True


class RunConfig(BaseModel):
    custom_benchmark_enabled: bool = True
    mode: str = "retrieval_only"
    approaches: list[str] = Field(default_factory=lambda: ["fractal_cli"])
    task_ids: list[str] = Field(default_factory=list)
    repetitions: int = 1
    simulate_reset: bool = True
    randomize_task_order: bool = False
    random_seed: int = 13
    output_directory: str = "benchmark_runner/outputs"
    corpus_directory: str = "benchmark_runner/corpus"
    tasks_directory: str = "benchmark_runner/tasks"
    use_memorybench_if_available: bool = False
    max_files_per_retrieval: int = 4
    fractal_cli_use_live_cli: bool = False
    fractal_cli_command: list[str] | None = None
    llm: LlmSettings = Field(default_factory=LlmSettings)
    providers: dict[str, ProviderEndpointConfig] = Field(default_factory=dict)
    answer_scoring: AnswerScoringConfig = Field(default_factory=AnswerScoringConfig)
    standard_benchmarks: StandardBenchmarksConfig = Field(default_factory=StandardBenchmarksConfig)


def load_config(path: str | Path) -> RunConfig:
    config_path = Path(path)
    with config_path.open("r", encoding="utf-8") as handle:
        data = yaml.safe_load(handle) or {}
    return RunConfig.model_validate(data)


def resolve_repo_root(start: str | Path | None = None) -> Path:
    candidates = [Path(start or Path.cwd()).resolve(), Path(__file__).resolve().parent.parent]
    for root in candidates:
        current = root
        while current != current.parent:
            if (current / "pyproject.toml").exists() and (current / "benchmark_runner").exists():
                return current
            current = current.parent
    raise FileNotFoundError("Could not locate FractalMemoryBench repository root.")


def expand_memorybench_runs(config: RunConfig, repo_root: str | Path | None = None) -> list[MemoryBenchRunConfig]:
    settings = config.standard_benchmarks
    if not settings.enabled or settings.backend != "memorybench":
        return []
    base = Path(repo_root) if repo_root is not None else resolve_repo_root()
    working_dir = Path(settings.memorybench.working_dir)
    if not working_dir.is_absolute():
        working_dir = (base / working_dir).resolve()
    runs: list[MemoryBenchRunConfig] = []
    for run in settings.memorybench.runs:
        runs.append(
            MemoryBenchRunConfig(
                mode=settings.memorybench.mode,
                executable=settings.memorybench.executable,
                working_dir=working_dir,
                benchmark=normalize_benchmark_name(run.benchmark),
                evaluator=run.evaluator,
                provider=run.provider,
                answering_model=run.answering_model,
                limit=run.limit,
                timeout_seconds=settings.memorybench.timeout_seconds,
                preserve_raw_outputs=settings.memorybench.preserve_raw_outputs,
                skip_if_unavailable=settings.memorybench.skip_if_unavailable,
                environment=settings.memorybench.environment,
            )
        )
    return runs
