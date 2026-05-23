from __future__ import annotations

from pathlib import Path

import pandas as pd
import typer
import yaml

from benchmark_runner.config import load_config, resolve_repo_root
from benchmark_runner.evaluators.answer_scorer import RuleBasedAnswerScorer
from benchmark_runner.llm import build_prompt, create_llm_provider, dry_run_prompt
from benchmark_runner.llm.answer_models import LlmResponse
from benchmark_runner.models import RetrievalItem, RetrievalResult
from benchmark_runner.reporting import (
    export_csv,
    export_markdown_report,
    infer_standard_benchmark_manifest,
    load_results_json,
    records_to_dataframe,
)
from benchmark_runner.runner import available_approaches, initialize_corpus, load_tasks, run_benchmark, validate_corpus
from benchmark_runner.scoring import score_task

app = typer.Typer(add_completion=False, no_args_is_help=True)


@app.command("init-corpus")
def init_corpus(destination: str | None = typer.Option(None, help="Optional target directory.")) -> None:
    repo_root = resolve_repo_root()
    target = Path(destination) if destination else repo_root / "benchmark_runner" / "corpus"
    initialize_corpus(target)
    typer.echo(f"Initialized corpus at {target}")


@app.command("list-tasks")
def list_tasks(config: str | None = typer.Option(None, help="Optional config file to filter task IDs.")) -> None:
    repo_root = resolve_repo_root()
    task_ids = load_config(config).task_ids if config else None
    tasks = load_tasks(repo_root / "benchmark_runner" / "tasks", task_ids or None)
    for task in tasks:
        typer.echo(f"{task.id}\t{task.category}\t{task.project}\t{task.prompt}")


@app.command("run")
def run(
    approach: str = typer.Option("all", help="Approach to run or 'all'."),
    config: str = typer.Option(..., help="Path to config YAML."),
    mode: str | None = typer.Option(None, help="Optional mode override: retrieval_only, end_to_end_llm, hybrid."),
) -> None:
    if approach != "all" and approach not in available_approaches():
        raise typer.BadParameter(f"Unknown approach '{approach}'.")
    outputs = run_benchmark_with_mode(config, approach=approach, mode_override=mode)
    typer.echo(f"JSON:   {outputs['json']}")
    typer.echo(f"CSV:    {outputs['csv']}")
    typer.echo(f"Report: {outputs['report']}")
    if "standard_benchmarks_manifest" in outputs:
        typer.echo(f"Standard Benchmarks: {outputs['standard_benchmarks_manifest']}")


@app.command("score")
def score(results_json: str = typer.Argument(..., help="Path to results.json.")) -> None:
    results = load_results_json(results_json)
    output_dir = Path(results_json).resolve().parent
    repo_root = resolve_repo_root(Path(results_json).resolve().parent)
    config = None
    answer_scorer = RuleBasedAnswerScorer()
    task_lookup = {task.id: task for task in load_tasks(repo_root / "benchmark_runner" / "tasks")}
    rescored_records = []
    for record in results.records:
        task = task_lookup[record.task_id]
        retrieval = RetrievalResult(
            approach_name=record.approach,
            retrieved_items=[
                RetrievalItem(path=path, content_excerpt="", relevant=None)
                for path in record.retrieved_paths
            ],
            files_opened=record.files_opened,
            lines_loaded=record.lines_loaded,
            estimated_tokens_loaded=record.tokens_loaded_est,
            retrieval_operations=record.retrieval_operations,
            raw_output=record.raw_output,
            metadata=record.adapter_metadata,
        )
        score_breakdown = score_task(task, retrieval)
        answer_update = {}
        if record.llm_response is not None:
            llm_response = LlmResponse.model_validate(record.llm_response.model_dump())
            answer_breakdown = answer_scorer.score(task, llm_response, retrieval)
            answer_update = {
                "final_answer_score_raw": answer_breakdown.final_answer_score_raw,
                "final_answer_score_adjusted": answer_breakdown.final_answer_score_adjusted,
                "answer_precision": answer_breakdown.answer_precision,
                "answer_recall": answer_breakdown.answer_recall,
                "task_completion_score": answer_breakdown.task_completion_score,
                "answer_contamination_penalty": answer_breakdown.contamination_penalty,
                "hallucinated_continuity_penalty": answer_breakdown.hallucinated_continuity_penalty,
                "unsupported_claim_penalty": answer_breakdown.unsupported_claim_penalty,
                "answer_score": answer_breakdown,
            }
        rescored_records.append(
            record.model_copy(
                update={
                    "resume_accuracy_raw": score_breakdown.resume_accuracy_raw,
                    "penalty_irrelevant_context": score_breakdown.penalty_irrelevant_context,
                    "penalty_contamination": score_breakdown.penalty_contamination,
                    "penalty_wrong_claim": score_breakdown.penalty_wrong_claim,
                    "resume_accuracy_adjusted": score_breakdown.resume_accuracy_adjusted,
                    "retrieval_precision": score_breakdown.retrieval_precision,
                    "retrieval_recall": score_breakdown.retrieval_recall,
                    "branch_purity": score_breakdown.branch_purity,
                    **answer_update,
                }
            )
        )
    frame = records_to_dataframe(rescored_records)
    export_csv(rescored_records, output_dir / "rescored.csv")
    export_markdown_report(frame, output_dir / "rescored-report.md", title=f"FractalMemoryBench Report ({results.manifest.run_id})")
    typer.echo(f"Re-scored {len(results.records)} records.")


@app.command("report")
def report(input_path: str = typer.Argument(..., help="Path to results.csv or results.json.")) -> None:
    path = Path(input_path)
    if path.suffix.lower() == ".json":
        results = load_results_json(path)
        frame = records_to_dataframe(results.records)
        standard_manifest_path = infer_standard_benchmark_manifest(path.parent)
    else:
        frame = pd.read_csv(path)
        standard_manifest_path = infer_standard_benchmark_manifest(path.parent)
    report_path = path.with_name(f"{path.stem}-summary.md")
    export_markdown_report(frame, report_path, standard_manifest_path=standard_manifest_path)
    typer.echo(f"Report: {report_path}")


@app.command("dry-run-prompt")
def dry_run_prompt_command(
    approach: str = typer.Option(..., help="Approach to simulate."),
    task: str = typer.Option(..., help="Task id."),
    config: str = typer.Option(..., help="Path to config YAML."),
) -> None:
    if approach not in available_approaches():
        raise typer.BadParameter(f"Unknown approach '{approach}'.")
    loaded_config = load_config(config)
    repo_root = resolve_repo_root(Path(config).resolve().parent)
    task_lookup = {item.id: item for item in load_tasks(repo_root / loaded_config.tasks_directory)}
    try:
        selected_task = task_lookup[task]
    except KeyError as exc:
        raise typer.BadParameter(f"Unknown task '{task}'.") from exc
    adapter = create_adapter_for_cli(approach, repo_root, loaded_config)
    try:
        retrieval = adapter.retrieve(selected_task.prompt, selected_task.model_dump())
    finally:
        adapter.cleanup()
    prompt = build_prompt(selected_task, retrieval, loaded_config.llm.model, loaded_config.llm.temperature, loaded_config.llm.max_tokens)
    typer.echo(dry_run_prompt(prompt))


@app.command("validate-corpus")
def validate() -> None:
    repo_root = resolve_repo_root()
    result = validate_corpus(repo_root / "benchmark_runner" / "corpus", repo_root / "benchmark_runner" / "tasks")
    typer.echo(f"Files checked: {result.files_checked}")
    if result.valid:
        typer.echo("Corpus validation passed.")
        return
    for issue in result.issues:
        typer.echo(f"- {issue}")
    raise typer.Exit(code=1)

def run_benchmark_with_mode(config_path: str, approach: str, mode_override: str | None):
    if mode_override is None:
        return run_benchmark(config_path, approach=approach)
    loaded = load_config(config_path)
    loaded.mode = mode_override
    temp_path = Path(config_path).resolve().parent / ".tmp-benchmark-config.yaml"
    try:
        temp_path.write_text(yaml.safe_dump(loaded.model_dump(mode="json")), encoding="utf-8")
        return run_benchmark(temp_path, approach=approach)
    finally:
        if temp_path.exists():
            temp_path.unlink()


def create_adapter_for_cli(approach: str, repo_root: Path, loaded_config):
    from benchmark_runner.runner import create_adapter

    adapter = create_adapter(approach)
    adapter.setup(repo_root / loaded_config.corpus_directory, repo_root / loaded_config.output_directory / "dry-run", loaded_config)
    return adapter


if __name__ == "__main__":
    app()
