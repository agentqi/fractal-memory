from __future__ import annotations

import json
import shutil
from pathlib import Path

import pandas as pd

from benchmark_runner.models import BenchmarkRecord, RunResults
from benchmark_runner.standard_benchmarks.models import MemoryBenchRunResult


CSV_COLUMNS = [
    "run_id",
    "date",
    "approach",
    "task_id",
    "task_category",
    "project",
    "resume_accuracy_raw",
    "penalty_irrelevant_context",
    "penalty_contamination",
    "penalty_wrong_claim",
    "resume_accuracy_adjusted",
    "files_opened",
    "lines_loaded",
    "tokens_loaded_est",
    "retrieval_operations",
    "depth_used",
    "resume_time_seconds",
    "retrieval_precision",
    "retrieval_recall",
    "branch_purity",
    "trust_score",
    "frustration_score",
    "cognitive_load_score",
    "helpfulness_score",
    "confidence_score",
    "llm_mode",
    "llm_provider",
    "llm_model",
    "llm_input_tokens",
    "llm_output_tokens",
    "llm_total_tokens",
    "llm_latency_seconds",
    "llm_cost_estimate",
    "final_answer_score_raw",
    "final_answer_score_adjusted",
    "format_adherence_score",
    "answer_precision",
    "answer_recall",
    "task_completion_score",
    "answer_contamination_penalty",
    "hallucinated_continuity_penalty",
    "unsupported_claim_penalty",
    "llm_response_text",
    "notes",
]


def records_to_dataframe(records: list[BenchmarkRecord]) -> pd.DataFrame:
    rows = [record.model_dump() for record in records]
    frame = pd.DataFrame(rows)
    for column in CSV_COLUMNS:
        if column not in frame.columns:
            frame[column] = None
    return frame


def export_csv(records: list[BenchmarkRecord], path: str | Path) -> Path:
    csv_path = Path(path)
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    frame = records_to_dataframe(records)
    frame.to_csv(csv_path, index=False, columns=CSV_COLUMNS)
    return csv_path


def export_json(results: RunResults, path: str | Path) -> Path:
    json_path = Path(path)
    json_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(results.model_dump_json(indent=2), encoding="utf-8")
    return json_path


def load_results_json(path: str | Path) -> RunResults:
    payload = json.loads(Path(path).read_text(encoding="utf-8"))
    return RunResults.model_validate(payload)


def load_standard_benchmark_manifest(path: str | Path) -> list[MemoryBenchRunResult]:
    manifest_path = Path(path)
    if not manifest_path.exists():
        return []
    payload = json.loads(manifest_path.read_text(encoding="utf-8"))
    if not isinstance(payload, list):
        return []
    return [MemoryBenchRunResult.model_validate(item) for item in payload]


def infer_standard_benchmark_manifest(run_directory: str | Path) -> Path | None:
    manifest_path = Path(run_directory) / "standard_benchmarks" / "memorybench" / "manifest.json"
    return manifest_path if manifest_path.exists() else None


def _build_standard_summary_frame(results: list[MemoryBenchRunResult]) -> pd.DataFrame:
    rows: list[dict[str, object]] = []
    for result in results:
        summary = result.summary
        notes = []
        if result.error:
            notes.append(result.error)
        if result.warnings:
            notes.append("; ".join(result.warnings))
        rows.append(
            {
                "backend": result.backend,
                "benchmark": result.benchmark,
                "approach": result.approach,
                "status": result.status,
                "primary_metric": summary.primary_metric if summary else None,
                "primary_score": summary.primary_score if summary else None,
                "total_questions": summary.total_questions if summary else 0,
                "total_latency_seconds": _load_standard_latency_seconds(result.questions_json_path),
                "notes": " | ".join(part for part in notes if part) or "",
            }
        )
    return pd.DataFrame(rows)


def _build_standard_detail_frame(results: list[MemoryBenchRunResult]) -> pd.DataFrame:
    rows: list[dict[str, object]] = []
    for result in results:
        summary = result.summary
        rows.append(
            {
                "benchmark": result.benchmark,
                "approach": result.approach,
                "status": result.status,
                "question_rows": _load_json_row_count(result.questions_json_path),
                "search_hit_rows": _load_json_row_count(result.search_hits_json_path),
                "primary_score": summary.primary_score if summary else None,
                "warnings": "; ".join(result.warnings) if result.warnings else "",
            }
        )
    return pd.DataFrame(rows)


def _load_json_row_count(path: str | None) -> int:
    if not path:
        return 0
    candidate = Path(path)
    if not candidate.exists():
        return 0
    payload = json.loads(candidate.read_text(encoding="utf-8"))
    return len(payload) if isinstance(payload, list) else 0


def _load_standard_latency_seconds(path: str | None) -> float | None:
    if not path:
        return None
    candidate = Path(path)
    if not candidate.exists():
        return None
    payload = json.loads(candidate.read_text(encoding="utf-8"))
    if not isinstance(payload, list):
        return None
    latencies = [
        float(item["latency_seconds"])
        for item in payload
        if isinstance(item, dict) and item.get("latency_seconds") is not None
    ]
    if not latencies:
        return None
    return round(sum(latencies), 3)


def generate_markdown_report(
    frame: pd.DataFrame,
    title: str = "FractalMemoryBench Report",
    *,
    standard_results: list[MemoryBenchRunResult] | None = None,
) -> str:
    lines: list[str] = [f"# {title}", ""]
    standard_results = standard_results or []
    lines.append("## Overview")
    lines.append("")
    lines.append(f"- Custom benchmark rows: {len(frame)}")
    lines.append(f"- Standard benchmark runs: {len(standard_results)}")
    lines.append(f"- Successful standard runs: {sum(1 for result in standard_results if result.status == 'succeeded')}")

    if len(frame) > 0:
        summary = frame.groupby("approach", dropna=False).agg(
            avg_adjusted=("resume_accuracy_adjusted", "mean"),
            avg_tokens=("tokens_loaded_est", "mean"),
            avg_resume_time=("resume_time_seconds", "mean"),
            avg_precision=("retrieval_precision", "mean"),
            avg_recall=("retrieval_recall", "mean"),
            avg_branch_purity=("branch_purity", "mean"),
        ).reset_index()

        lines.append("")
        lines.append("## Custom Benchmarks")
        lines.append("")
        lines.append("### Approach Summary")
        lines.append("")
        lines.append(summary.to_markdown(index=False, floatfmt=".2f"))

        lines.append("")
        failures = frame.sort_values(
            ["resume_accuracy_adjusted", "branch_purity", "retrieval_precision"],
            ascending=[True, True, True],
        ).head(5)[["approach", "task_id", "resume_accuracy_adjusted", "branch_purity", "notes"]]
        lines.append("### Top Failures")
        lines.append("")
        lines.append(failures.to_markdown(index=False, floatfmt=".2f"))

        breakdown = frame.groupby(["approach", "task_category"], dropna=False).agg(
            avg_adjusted=("resume_accuracy_adjusted", "mean"),
            avg_tokens=("tokens_loaded_est", "mean"),
            avg_branch_purity=("branch_purity", "mean"),
        ).reset_index()
        lines.append("")
        lines.append("### Task Family Breakdown")
        lines.append("")
        lines.append(breakdown.to_markdown(index=False, floatfmt=".2f"))

        if "llm_mode" in frame.columns and frame["llm_mode"].fillna("retrieval_only").ne("retrieval_only").any():
            llm_summary = frame.groupby("approach", dropna=False).agg(
                avg_answer_score=("final_answer_score_adjusted", "mean"),
                avg_format_adherence=("format_adherence_score", "mean"),
                avg_total_llm_tokens=("llm_total_tokens", "mean"),
                avg_latency=("llm_latency_seconds", "mean"),
                avg_task_completion=("task_completion_score", "mean"),
                avg_contamination_penalty=("answer_contamination_penalty", "mean"),
            ).reset_index()
            lines.append("")
            lines.append("### End-To-End LLM Summary")
            lines.append("")
            lines.append(llm_summary.to_markdown(index=False, floatfmt=".2f"))

            comparison = frame.groupby("approach", dropna=False).agg(
                retrieval_score=("resume_accuracy_adjusted", "mean"),
                answer_score=("final_answer_score_adjusted", "mean"),
                retrieval_tokens=("tokens_loaded_est", "mean"),
                llm_total_tokens=("llm_total_tokens", "mean"),
                branch_purity=("branch_purity", "mean"),
                task_completion=("task_completion_score", "mean"),
            ).reset_index()
            lines.append("")
            lines.append("### Retrieval vs Answer Comparison")
            lines.append("")
            lines.append(comparison.to_markdown(index=False, floatfmt=".2f"))

    if standard_results:
        standard_summary = _build_standard_summary_frame(standard_results)
        lines.append("")
        lines.append("## Standard Benchmarks")
        lines.append("")
        lines.append(standard_summary.to_markdown(index=False, floatfmt=".2f"))

        standard_details = _build_standard_detail_frame(standard_results)
        lines.append("")
        lines.append("## Standard Benchmark Details")
        lines.append("")
        lines.append(standard_details.to_markdown(index=False, floatfmt=".2f"))

    return "\n".join(lines)


def export_markdown_report(
    frame: pd.DataFrame,
    path: str | Path,
    title: str = "FractalMemoryBench Report",
    *,
    standard_results: list[MemoryBenchRunResult] | None = None,
    standard_manifest_path: str | Path | None = None,
) -> Path:
    report_path = Path(path)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    resolved_standard_results = standard_results
    if resolved_standard_results is None and standard_manifest_path is not None:
        resolved_standard_results = load_standard_benchmark_manifest(standard_manifest_path)
    report_path.write_text(
        generate_markdown_report(frame, title=title, standard_results=resolved_standard_results),
        encoding="utf-8",
    )
    return report_path


def update_latest_directory(run_directory: Path, latest_directory: Path) -> None:
    if latest_directory.exists():
        shutil.rmtree(latest_directory)
    shutil.copytree(run_directory, latest_directory)
