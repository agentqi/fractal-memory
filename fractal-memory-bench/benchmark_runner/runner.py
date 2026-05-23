from __future__ import annotations

import random
import shutil
from datetime import datetime, timezone
from pathlib import Path
from time import perf_counter

import yaml

from benchmark_runner.adapters import (
    AutoRetrievalStubAdapter,
    FlatFoldersAdapter,
    FractalCliAdapter,
    McpAdapterStub,
    MemoryBenchAdapter,
    NoPersistenceAdapter,
    SingleFileAdapter,
)
from benchmark_runner.adapters.base import MemoryApproachAdapter
from benchmark_runner.config import RunConfig, expand_memorybench_runs, load_config, resolve_repo_root
from benchmark_runner.evaluators.answer_scorer import RuleBasedAnswerScorer
from benchmark_runner.llm import build_prompt, create_llm_provider
from benchmark_runner.models import (
    AnswerScore,
    BenchmarkRecord,
    CorpusValidationResult,
    LlmRequestRecord,
    LlmResponseRecord,
    RunManifest,
    RunResults,
    TaskDefinition,
)
from benchmark_runner.reporting import (
    export_csv,
    export_json,
    export_markdown_report,
    load_standard_benchmark_manifest,
    update_latest_directory,
)
from benchmark_runner.reset_simulator import simulate_reset
from benchmark_runner.scoring import score_task
from benchmark_runner.standard_benchmarks.memorybench_bridge import (
    MemoryBenchBridge,
    write_standard_benchmark_manifest,
)


ADAPTER_REGISTRY: dict[str, type[MemoryApproachAdapter]] = {
    "no_persistence": NoPersistenceAdapter,
    "single_file": SingleFileAdapter,
    "flat_folders": FlatFoldersAdapter,
    "fractal_cli": FractalCliAdapter,
    "auto_retrieval_stub": AutoRetrievalStubAdapter,
    "memorybench_adapter": MemoryBenchAdapter,
    "mcp_adapter_stub": McpAdapterStub,
}


def available_approaches() -> list[str]:
    return sorted(ADAPTER_REGISTRY)


def load_tasks(tasks_directory: str | Path, task_ids: list[str] | None = None) -> list[TaskDefinition]:
    directory = Path(tasks_directory)
    tasks: list[TaskDefinition] = []
    for path in sorted(directory.glob("*.yaml")):
        with path.open("r", encoding="utf-8") as handle:
            payload = yaml.safe_load(handle) or {}
        task = TaskDefinition.model_validate(payload)
        if task_ids and task.id not in task_ids:
            continue
        tasks.append(task)
    return tasks


def validate_corpus(corpus_path: str | Path, tasks_directory: str | Path | None = None) -> CorpusValidationResult:
    path = Path(corpus_path)
    required = [
        "root/index.md",
        "root/state.md",
        "projects/fractal-memory-cli/index.md",
        "projects/govos/index.md",
        "projects/flowone/index.md",
        "systems/architecture/index.md",
        "handoffs/latest-fractal-memory-cli.md",
        "handoffs/latest-govos.md",
    ]
    issues: list[str] = []
    files_checked = 0
    for relative in required:
        files_checked += 1
        if not (path / relative).exists():
            issues.append(f"Missing corpus file: {relative}")

    if tasks_directory is not None:
        tasks = load_tasks(tasks_directory)
        files_checked += len(tasks)
        if len(tasks) != 12:
            issues.append(f"Expected 12 tasks, found {len(tasks)}.")

    return CorpusValidationResult(valid=not issues, issues=issues, files_checked=files_checked)


def initialize_corpus(target_directory: str | Path) -> Path:
    repo_root = resolve_repo_root()
    source = repo_root / "benchmark_runner" / "corpus"
    target = Path(target_directory)
    if target.exists():
        shutil.rmtree(target)
    shutil.copytree(source, target)
    return target


def create_adapter(name: str) -> MemoryApproachAdapter:
    try:
        adapter_type = ADAPTER_REGISTRY[name]
    except KeyError as exc:
        raise ValueError(f"Unknown approach '{name}'. Available: {', '.join(available_approaches())}") from exc
    return adapter_type()


def run_benchmark(config_path: str | Path, approach: str = "all") -> dict[str, Path]:
    repo_root = resolve_repo_root(Path(config_path).resolve().parent)
    config = load_config(config_path)
    approaches = config.approaches if approach == "all" else [approach]
    tasks = load_tasks(repo_root / config.tasks_directory, config.task_ids or None) if config.custom_benchmark_enabled else []
    if config.randomize_task_order and tasks:
        random.Random(config.random_seed).shuffle(tasks)

    timestamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    run_id = f"run-{timestamp}"
    output_root = repo_root / config.output_directory
    run_dir = output_root / run_id
    latest_dir = output_root / "latest"
    run_dir.mkdir(parents=True, exist_ok=True)

    records: list[BenchmarkRecord] = []
    llm_provider = create_llm_provider(config) if _should_run_llm(config) else None
    answer_scorer = RuleBasedAnswerScorer() if _should_run_llm(config) and config.answer_scoring.use_rule_based_scorer else None
    if tasks:
        for approach_name in approaches:
            adapter = create_adapter(approach_name)
            adapter.setup(repo_root / config.corpus_directory, run_dir / approach_name, config)
            try:
                for repetition in range(1, config.repetitions + 1):
                    for task in tasks:
                        if config.simulate_reset:
                            retrieval, resume_time = simulate_reset(adapter, task.prompt, task.model_dump())
                        else:
                            start = perf_counter()
                            retrieval = adapter.retrieve(task.prompt, task.model_dump())
                            resume_time = perf_counter() - start
                        score = score_task(task, retrieval)
                        llm_request_record: LlmRequestRecord | None = None
                        llm_response_record: LlmResponseRecord | None = None
                        answer_score: AnswerScore | None = None
                        if _should_run_llm(config):
                            prompt = build_prompt(
                                task,
                                retrieval,
                                model_name=config.llm.model,
                                temperature=config.llm.temperature,
                                max_tokens=config.llm.max_tokens,
                            )
                            llm_request_record = LlmRequestRecord.model_validate(prompt.model_dump())
                            response = llm_provider.generate(prompt) if llm_provider is not None else None
                            if response is not None:
                                llm_response_record = LlmResponseRecord.model_validate(response.model_dump())
                                if answer_scorer is not None:
                                    answer_score = answer_scorer.score(task, response, retrieval)
                        records.append(
                            BenchmarkRecord(
                                run_id=run_id,
                                date=datetime.now(timezone.utc).date().isoformat(),
                                approach=approach_name,
                                task_id=task.id,
                                task_category=task.category,
                                project=task.project,
                                repetition=repetition,
                                resume_accuracy_raw=score.resume_accuracy_raw,
                                penalty_irrelevant_context=score.penalty_irrelevant_context,
                                penalty_contamination=score.penalty_contamination,
                                penalty_wrong_claim=score.penalty_wrong_claim,
                                resume_accuracy_adjusted=score.resume_accuracy_adjusted,
                                files_opened=retrieval.files_opened,
                                lines_loaded=retrieval.lines_loaded,
                                tokens_loaded_est=retrieval.estimated_tokens_loaded,
                                retrieval_operations=retrieval.retrieval_operations,
                                depth_used=str(retrieval.metadata.get("depth_used", "n/a")),
                                resume_time_seconds=round(resume_time, 6),
                                retrieval_precision=score.retrieval_precision,
                                retrieval_recall=score.retrieval_recall,
                                branch_purity=score.branch_purity,
                                trust_score=score.trust_score,
                                frustration_score=score.frustration_score,
                                cognitive_load_score=score.cognitive_load_score,
                                helpfulness_score=score.helpfulness_score,
                                confidence_score=score.confidence_score,
                                llm_mode=config.mode,
                                llm_provider=llm_response_record.provider_name if llm_response_record else None,
                                llm_model=llm_response_record.model_name if llm_response_record else None,
                                llm_input_tokens=llm_response_record.input_tokens if llm_response_record else 0,
                                llm_output_tokens=llm_response_record.output_tokens if llm_response_record else 0,
                                llm_total_tokens=llm_response_record.total_tokens if llm_response_record else 0,
                                llm_latency_seconds=llm_response_record.latency_seconds if llm_response_record else 0.0,
                                llm_cost_estimate=llm_response_record.cost_estimate if llm_response_record else 0.0,
                                final_answer_score_raw=answer_score.final_answer_score_raw if answer_score else 0,
                                final_answer_score_adjusted=answer_score.final_answer_score_adjusted if answer_score else 0,
                                format_adherence_score=answer_score.format_adherence_score if answer_score else 0,
                                answer_precision=answer_score.answer_precision if answer_score else 0.0,
                                answer_recall=answer_score.answer_recall if answer_score else 0.0,
                                task_completion_score=answer_score.task_completion_score if answer_score else 0,
                                answer_contamination_penalty=answer_score.contamination_penalty if answer_score else 0,
                                hallucinated_continuity_penalty=answer_score.hallucinated_continuity_penalty if answer_score else 0,
                                unsupported_claim_penalty=answer_score.unsupported_claim_penalty if answer_score else 0,
                                llm_response_text=llm_response_record.content if llm_response_record else None,
                                notes=score.notes or str(retrieval.metadata.get("notes", "")),
                                raw_output=retrieval.raw_output,
                                retrieved_paths=[item.path for item in retrieval.retrieved_items],
                                adapter_metadata=retrieval.metadata,
                                llm_request=llm_request_record,
                                llm_response=llm_response_record,
                                answer_score=answer_score,
                            )
                        )
            finally:
                adapter.cleanup()

    manifest = RunManifest(
        run_id=run_id,
        created_at=datetime.now(timezone.utc),
        config_name=Path(config_path).name,
        approaches=approaches,
        task_ids=[task.id for task in tasks],
        mode=config.mode,
        simulate_reset=config.simulate_reset,
        repetitions=config.repetitions,
        output_directory=str(output_root),
    )
    results = RunResults(manifest=manifest, records=records)

    json_path = export_json(results, run_dir / "results.json")
    csv_path = export_csv(records, run_dir / "results.csv")
    standard_manifest_path = run_standard_benchmarks(
        config=config,
        approaches=approaches,
        repo_root=repo_root,
        run_dir=run_dir,
    )
    standard_results = load_standard_benchmark_manifest(standard_manifest_path) if standard_manifest_path else []
    report_path = export_markdown_report(
        export_csv_and_load_frame(records, run_dir / "results.csv"),
        run_dir / "report.md",
        title=f"FractalMemoryBench Report ({run_id})",
        standard_results=standard_results,
    )
    update_latest_directory(run_dir, latest_dir)
    outputs = {"json": json_path, "csv": csv_path, "report": report_path, "run_dir": run_dir, "latest_dir": latest_dir}
    if standard_manifest_path is not None:
        outputs["standard_benchmarks_manifest"] = standard_manifest_path
    return outputs


def export_csv_and_load_frame(records: list[BenchmarkRecord], csv_path: Path):
    export_csv(records, csv_path)
    import pandas as pd

    return pd.read_csv(csv_path)


def _should_run_llm(config: RunConfig) -> bool:
    return config.mode in {"end_to_end_llm", "hybrid"} and config.llm.enabled


def run_standard_benchmarks(
    *,
    config: RunConfig,
    approaches: list[str],
    repo_root: Path,
    run_dir: Path,
) -> Path | None:
    memorybench_runs = expand_memorybench_runs(config, repo_root=repo_root)
    if not memorybench_runs:
        return None
    bridge = MemoryBenchBridge()
    results = []
    standard_root = run_dir / "standard_benchmarks" / "memorybench"
    for approach_name in approaches:
        adapter = create_adapter(approach_name)
        adapter.setup(repo_root / config.corpus_directory, run_dir / f"{approach_name}-standard", config)
        try:
            for run_config in memorybench_runs:
                results.append(
                    bridge.run(
                        approach_name=approach_name,
                        adapter=adapter,
                        run_config=run_config,
                        output_dir=str(standard_root),
                    )
                )
        finally:
            adapter.cleanup()
    return write_standard_benchmark_manifest(results, standard_root / "manifest.json")
