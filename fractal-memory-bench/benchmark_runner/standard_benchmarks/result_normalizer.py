from __future__ import annotations

import csv
import json
from pathlib import Path
from typing import Any

from benchmark_runner.standard_benchmarks.models import (
    MemoryBenchAggregateSummary,
    MemoryBenchQuestionRow,
    MemoryBenchSearchHitRow,
    NormalizedMemoryBenchRun,
)


def normalize_memorybench_run(
    run_dir: Path,
    *,
    run_id: str,
    benchmark: str,
    approach: str,
    evaluator: str,
) -> NormalizedMemoryBenchRun:
    warnings: list[str] = []
    summary, report_data = load_authoritative_report(run_dir / "report.json", benchmark=benchmark, approach=approach)
    summary_warnings = []
    questions = extract_question_rows(
        run_dir,
        report_data,
        run_id=run_id,
        benchmark=benchmark,
        approach=approach,
        evaluator=evaluator,
        warnings=summary_warnings,
    )
    search_hits = extract_search_hit_rows(
        run_dir,
        run_id=run_id,
        benchmark=benchmark,
        approach=approach,
        warnings=summary_warnings,
    )
    warnings.extend(summary_warnings)
    warnings.extend(detect_question_count_conflict(summary, questions))
    if not questions:
        warnings.append("No per-question evaluation records found; summary-only normalization produced.")
    summary.warnings.extend(warnings)
    return NormalizedMemoryBenchRun(
        summary=summary,
        questions=questions,
        search_hits=search_hits,
        warnings=warnings,
    )


def normalize_run_directory(
    raw_dir: str | Path,
    *,
    run_id: str,
    benchmark: str,
    approach: str,
    evaluator: str,
) -> tuple[NormalizedMemoryBenchRun, list[str]]:
    run = normalize_memorybench_run(
        Path(raw_dir),
        run_id=run_id,
        benchmark=benchmark,
        approach=approach,
        evaluator=evaluator,
    )
    return run, run.warnings


def load_authoritative_report(
    report_path: Path,
    *,
    benchmark: str,
    approach: str,
) -> tuple[MemoryBenchAggregateSummary, dict[str, Any]]:
    if not report_path.exists():
        raise FileNotFoundError(f"No authoritative MemoryBench report found at {report_path}")
    report_data = json.loads(report_path.read_text(encoding="utf-8"))
    summary_payload = report_data.get("summary") if isinstance(report_data.get("summary"), dict) else {}
    primary_metric = "accuracy" if "accuracy" in summary_payload else "score"
    primary_score = _coerce_float(summary_payload.get("accuracy"))
    secondary_metrics: dict[str, float] = {}
    for key in ("correctCount", "totalQuestions"):
        value = _coerce_float(summary_payload.get(key))
        if value is not None:
            secondary_metrics[key] = value
    for source in ("memscoreComponents", "retrieval"):
        payload = report_data.get(source)
        if isinstance(payload, dict):
            for key, value in payload.items():
                coerced = _coerce_float(value)
                if coerced is not None:
                    secondary_metrics[f"{source}.{key}"] = coerced
    token_payload = report_data.get("tokens")
    if isinstance(token_payload, dict):
        for key, value in token_payload.items():
            coerced = _coerce_float(value)
            if coerced is not None:
                secondary_metrics[f"tokens.{key}"] = coerced
    total_questions = int(summary_payload.get("totalQuestions") or len(report_data.get("evaluations") or []))
    succeeded_questions = len(report_data.get("evaluations") or [])
    failed_questions = max(total_questions - succeeded_questions, 0)
    return (
        MemoryBenchAggregateSummary(
            benchmark=benchmark,
            approach=approach,
            primary_metric=primary_metric,
            primary_score=primary_score,
            secondary_metrics=secondary_metrics,
            total_questions=total_questions,
            succeeded_questions=succeeded_questions,
            failed_questions=failed_questions,
            raw_report_path=str(report_path),
            warnings=[],
        ),
        report_data,
    )


def extract_question_rows(
    run_dir: Path,
    report_data: dict[str, Any],
    *,
    run_id: str,
    benchmark: str,
    approach: str,
    evaluator: str,
    warnings: list[str],
) -> list[MemoryBenchQuestionRow]:
    evaluations = report_data.get("evaluations") if isinstance(report_data.get("evaluations"), list) else []
    if not evaluations:
        return []
    result_dir = run_dir / "results"
    rows: list[MemoryBenchQuestionRow] = []
    for evaluation in evaluations:
        if not isinstance(evaluation, dict):
            continue
        question_id = str(evaluation.get("questionId") or "")
        raw_result_path = result_dir / f"{question_id}.json"
        raw_payload: dict[str, Any] | None = None
        if raw_result_path.exists():
            try:
                raw_payload = json.loads(raw_result_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                warnings.append(f"Malformed per-question result file: {raw_result_path}")
        else:
            warnings.append(f"Missing per-question result file for {question_id}: {raw_result_path}")

        search_hit_count = None
        if raw_payload is not None and isinstance(raw_payload.get("results"), list):
            search_hit_count = len(raw_payload["results"])
            warnings.append(
                f"Detected nested search hits in {raw_result_path.name}; preserving as search diagnostics only."
            )

        retrieval_metrics = evaluation.get("retrievalMetrics") if isinstance(evaluation.get("retrievalMetrics"), dict) else {}
        extra_metrics: dict[str, Any] = {
            "label": evaluation.get("label"),
            "question_type": evaluation.get("questionType"),
        }
        for key, value in retrieval_metrics.items():
            extra_metrics[f"retrieval.{key}"] = value
        question_row = MemoryBenchQuestionRow(
            run_id=run_id,
            benchmark=benchmark,
            approach=approach,
            question_id=question_id,
            evaluator=evaluator,
            primary_metric="score",
            primary_score=_coerce_float(evaluation.get("score")),
            evaluation_status="completed",
            latency_seconds=_coerce_millis_to_seconds(evaluation.get("totalDurationMs")),
            input_tokens=None,
            output_tokens=None,
            total_tokens=None,
            raw_result_path=str(raw_result_path if raw_result_path.exists() else run_dir / "report.json"),
            search_hit_count=search_hit_count,
            extra_metrics=extra_metrics,
        )
        rows.append(question_row)
    return rows


def extract_search_hit_rows(
    run_dir: Path,
    *,
    run_id: str,
    benchmark: str,
    approach: str,
    warnings: list[str],
) -> list[MemoryBenchSearchHitRow]:
    result_dir = run_dir / "results"
    if not result_dir.exists():
        return []
    rows: list[MemoryBenchSearchHitRow] = []
    for raw_result_path in sorted(result_dir.glob("*.json")):
        try:
            payload = json.loads(raw_result_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            warnings.append(f"Malformed per-question result file: {raw_result_path}")
            continue
        question_id = str(payload.get("questionId") or raw_result_path.stem)
        hits = payload.get("results")
        if not isinstance(hits, list):
            continue
        for index, hit in enumerate(hits, start=1):
            if not isinstance(hit, dict):
                warnings.append(f"Malformed search-hit payload in {raw_result_path.name}; skipping hit {index}.")
                continue
            rows.append(
                MemoryBenchSearchHitRow(
                    run_id=run_id,
                    benchmark=benchmark,
                    approach=approach,
                    question_id=question_id,
                    hit_rank=index,
                    hit_id=str(hit.get("title") or hit.get("sessionId") or f"{question_id}-hit-{index}"),
                    score=_coerce_float(hit.get("score")),
                    is_relevant=None,
                    session_id=str(hit.get("sessionId")) if hit.get("sessionId") is not None else None,
                    source_path=str(hit.get("sessionId")) if hit.get("sessionId") is not None else None,
                    snippet=_build_hit_snippet(hit),
                    raw_result_path=str(raw_result_path),
                )
            )
    return rows


def detect_question_count_conflict(
    report_summary: MemoryBenchAggregateSummary,
    question_rows: list[MemoryBenchQuestionRow],
) -> list[str]:
    if report_summary.total_questions != len(question_rows) and question_rows:
        return [
            "Authoritative report total_questions="
            f"{report_summary.total_questions} conflicts with reconstructed question_rows={len(question_rows)}; using report value."
        ]
    return []


def write_normalized_outputs(
    normalized: NormalizedMemoryBenchRun,
    output_dir: str | Path,
) -> dict[str, Path]:
    target = Path(output_dir)
    target.mkdir(parents=True, exist_ok=True)

    combined_json = target / "normalized.json"
    summary_json = target / "summary.json"
    questions_json = target / "questions.json"
    search_hits_json = target / "search_hits.json"
    summary_csv = target / "summary.csv"
    questions_csv = target / "questions.csv"
    search_hits_csv = target / "search_hits.csv"
    combined_csv = target / "normalized.csv"

    combined_payload = {
        "summary": normalized.summary.model_dump(mode="json"),
        "questions": [row.model_dump(mode="json") for row in normalized.questions],
        "search_hits": [row.model_dump(mode="json") for row in normalized.search_hits],
    }
    combined_json.write_text(json.dumps(combined_payload, indent=2), encoding="utf-8")
    summary_json.write_text(json.dumps(normalized.summary.model_dump(mode="json"), indent=2), encoding="utf-8")
    questions_json.write_text(
        json.dumps([row.model_dump(mode="json") for row in normalized.questions], indent=2),
        encoding="utf-8",
    )
    search_hits_json.write_text(
        json.dumps([row.model_dump(mode="json") for row in normalized.search_hits], indent=2),
        encoding="utf-8",
    )

    _write_csv(
        summary_csv,
        [normalized.summary.model_dump(mode="json")],
        [
            "track",
            "backend",
            "benchmark",
            "approach",
            "primary_metric",
            "primary_score",
            "secondary_metrics",
            "total_questions",
            "succeeded_questions",
            "failed_questions",
            "raw_report_path",
            "warnings",
        ],
    )
    _write_csv(
        questions_csv,
        [row.model_dump(mode="json") for row in normalized.questions],
        [
            "run_id",
            "track",
            "backend",
            "benchmark",
            "approach",
            "question_id",
            "evaluator",
            "primary_metric",
            "primary_score",
            "evaluation_status",
            "input_tokens",
            "output_tokens",
            "total_tokens",
            "latency_seconds",
            "raw_result_path",
            "search_hit_count",
            "extra_metrics",
        ],
    )
    _write_csv(
        search_hits_csv,
        [row.model_dump(mode="json") for row in normalized.search_hits],
        [
            "run_id",
            "track",
            "backend",
            "benchmark",
            "approach",
            "question_id",
            "hit_rank",
            "hit_id",
            "score",
            "is_relevant",
            "session_id",
            "source_path",
            "snippet",
            "raw_result_path",
        ],
    )
    # Backward-compatible flat CSV points to per-question rows only.
    _write_csv(
        combined_csv,
        [row.model_dump(mode="json") for row in normalized.questions],
        [
            "run_id",
            "track",
            "backend",
            "benchmark",
            "approach",
            "question_id",
            "evaluator",
            "primary_metric",
            "primary_score",
            "evaluation_status",
            "input_tokens",
            "output_tokens",
            "total_tokens",
            "latency_seconds",
            "raw_result_path",
            "search_hit_count",
            "extra_metrics",
        ],
    )
    return {
        "normalized_json": combined_json,
        "normalized_csv": combined_csv,
        "summary_json": summary_json,
        "summary_csv": summary_csv,
        "questions_json": questions_json,
        "questions_csv": questions_csv,
        "search_hits_json": search_hits_json,
        "search_hits_csv": search_hits_csv,
    }


def _write_csv(path: Path, rows: list[dict[str, Any]], fieldnames: list[str]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        for row in rows:
            payload = dict(row)
            for key, value in list(payload.items()):
                if isinstance(value, (dict, list)):
                    payload[key] = json.dumps(value, sort_keys=True)
            writer.writerow(payload)


def _coerce_float(value: Any) -> float | None:
    if value is None or value == "":
        return None
    return float(value)


def _coerce_millis_to_seconds(value: Any) -> float | None:
    coerced = _coerce_float(value)
    if coerced is None:
        return None
    return round(coerced / 1000.0, 6)


def _build_hit_snippet(hit: dict[str, Any]) -> str | None:
    for key in ("timeline_excerpt", "state", "index", "decisions"):
        value = hit.get(key)
        if isinstance(value, str) and value.strip():
            compact = " ".join(value.strip().split())
            return compact[:400]
    return None
