from __future__ import annotations

import json
from pathlib import Path

from benchmark_runner.standard_benchmarks.result_normalizer import (
    detect_question_count_conflict,
    normalize_memorybench_run,
)


def _write_report(run_dir: Path, *, total_questions: int, evaluations: list[dict]) -> None:
    payload = {
        "provider": "fractalmemory",
        "benchmark": "locomo",
        "runId": "run-123",
        "summary": {
            "totalQuestions": total_questions,
            "correctCount": sum(1 for item in evaluations if item.get("score") == 1),
            "accuracy": sum(item.get("score", 0) for item in evaluations) / max(total_questions, 1),
        },
        "evaluations": evaluations,
    }
    (run_dir / "report.json").write_text(json.dumps(payload), encoding="utf-8")


def _write_question_result(run_dir: Path, question_id: str, hits: list[dict]) -> None:
    results_dir = run_dir / "results"
    results_dir.mkdir(parents=True, exist_ok=True)
    payload = {
        "questionId": question_id,
        "question": "When did Caroline go to the LGBTQ support group?",
        "questionType": "multi-hop",
        "groundTruth": "7 May 2023",
        "results": hits,
    }
    (results_dir / f"{question_id}.json").write_text(json.dumps(payload), encoding="utf-8")


def test_single_question_with_ten_search_hits_is_normalized_correctly(tmp_path: Path) -> None:
    run_dir = tmp_path / "run-123"
    run_dir.mkdir()
    _write_report(
        run_dir,
        total_questions=1,
        evaluations=[
            {
                "questionId": "conv-26-q0",
                "questionType": "multi-hop",
                "score": 1,
                "label": "correct",
                "totalDurationMs": 91417,
                "retrievalMetrics": {"hitAtK": 1, "mrr": 0.5},
            }
        ],
    )
    _write_question_result(
        run_dir,
        "conv-26-q0",
        [{"sessionId": f"s{i}", "score": 1 / (i + 1), "timeline_excerpt": f"hit {i}"} for i in range(10)],
    )

    normalized = normalize_memorybench_run(
        run_dir,
        run_id="run-123",
        benchmark="locomo",
        approach="fractal_cli",
        evaluator="gpt-4o",
    )

    assert normalized.summary.total_questions == 1
    assert normalized.summary.primary_score == 1.0
    assert len(normalized.questions) == 1
    assert normalized.questions[0].question_id == "conv-26-q0"
    assert normalized.questions[0].search_hit_count == 10
    assert len(normalized.search_hits) == 10
    assert normalized.search_hits[0].question_id == "conv-26-q0"


def test_multi_question_run_emits_one_question_row_per_evaluated_question(tmp_path: Path) -> None:
    run_dir = tmp_path / "run-123"
    run_dir.mkdir()
    _write_report(
        run_dir,
        total_questions=2,
        evaluations=[
            {"questionId": "q1", "questionType": "single-hop", "score": 1, "label": "correct", "totalDurationMs": 1000},
            {"questionId": "q2", "questionType": "multi-hop", "score": 0, "label": "incorrect", "totalDurationMs": 2000},
        ],
    )
    _write_question_result(run_dir, "q1", [{"sessionId": "a", "score": 0.9, "timeline_excerpt": "alpha"}])
    _write_question_result(
        run_dir,
        "q2",
        [
            {"sessionId": "b", "score": 0.7, "timeline_excerpt": "beta"},
            {"sessionId": "c", "score": 0.6, "timeline_excerpt": "gamma"},
        ],
    )

    normalized = normalize_memorybench_run(
        run_dir,
        run_id="run-123",
        benchmark="locomo",
        approach="fractal_cli",
        evaluator="gpt-4o",
    )

    assert normalized.summary.total_questions == 2
    assert len(normalized.questions) == 2
    assert {row.question_id for row in normalized.questions} == {"q1", "q2"}
    assert len(normalized.search_hits) == 3


def test_report_count_wins_when_question_rows_conflict(tmp_path: Path) -> None:
    run_dir = tmp_path / "run-123"
    run_dir.mkdir()
    _write_report(
        run_dir,
        total_questions=1,
        evaluations=[
            {"questionId": "q1", "questionType": "single-hop", "score": 1, "label": "correct", "totalDurationMs": 1000},
            {"questionId": "q2", "questionType": "single-hop", "score": 1, "label": "correct", "totalDurationMs": 1000},
        ],
    )
    _write_question_result(run_dir, "q1", [{"sessionId": "a", "score": 0.9, "timeline_excerpt": "alpha"}])
    _write_question_result(run_dir, "q2", [{"sessionId": "b", "score": 0.9, "timeline_excerpt": "beta"}])

    normalized = normalize_memorybench_run(
        run_dir,
        run_id="run-123",
        benchmark="locomo",
        approach="fractal_cli",
        evaluator="gpt-4o",
    )

    assert normalized.summary.total_questions == 1
    assert any("conflicts with reconstructed question_rows=2" in warning for warning in normalized.warnings)


def test_missing_per_question_files_produces_summary_only_warning(tmp_path: Path) -> None:
    run_dir = tmp_path / "run-123"
    run_dir.mkdir()
    _write_report(
        run_dir,
        total_questions=1,
        evaluations=[
            {"questionId": "q1", "questionType": "single-hop", "score": 1, "label": "correct", "totalDurationMs": 1000}
        ],
    )

    normalized = normalize_memorybench_run(
        run_dir,
        run_id="run-123",
        benchmark="locomo",
        approach="fractal_cli",
        evaluator="gpt-4o",
    )

    assert normalized.summary.total_questions == 1
    assert len(normalized.questions) == 1
    assert len(normalized.search_hits) == 0
    assert any("Missing per-question result file for q1" in warning for warning in normalized.warnings)


def test_malformed_search_hit_payload_is_skipped_without_inflating_question_count(tmp_path: Path) -> None:
    run_dir = tmp_path / "run-123"
    run_dir.mkdir()
    _write_report(
        run_dir,
        total_questions=1,
        evaluations=[
            {"questionId": "q1", "questionType": "single-hop", "score": 1, "label": "correct", "totalDurationMs": 1000}
        ],
    )
    _write_question_result(run_dir, "q1", [{"sessionId": "a", "score": 0.9, "timeline_excerpt": "alpha"}, "bad-hit"])

    normalized = normalize_memorybench_run(
        run_dir,
        run_id="run-123",
        benchmark="locomo",
        approach="fractal_cli",
        evaluator="gpt-4o",
    )

    assert normalized.summary.total_questions == 1
    assert len(normalized.questions) == 1
    assert len(normalized.search_hits) == 1
    assert any("Malformed search-hit payload" in warning for warning in normalized.warnings)


def test_detect_question_count_conflict_helper() -> None:
    from benchmark_runner.standard_benchmarks.models import MemoryBenchAggregateSummary, MemoryBenchQuestionRow

    summary = MemoryBenchAggregateSummary(
        benchmark="locomo",
        approach="fractal_cli",
        primary_metric="accuracy",
        primary_score=1.0,
        total_questions=1,
        succeeded_questions=1,
        failed_questions=0,
        raw_report_path="/tmp/report.json",
    )
    rows = [
        MemoryBenchQuestionRow(
            run_id="run-123",
            benchmark="locomo",
            approach="fractal_cli",
            question_id="q1",
            evaluator="gpt-4o",
            primary_metric="score",
            primary_score=1.0,
            raw_result_path="/tmp/q1.json",
        ),
        MemoryBenchQuestionRow(
            run_id="run-123",
            benchmark="locomo",
            approach="fractal_cli",
            question_id="q2",
            evaluator="gpt-4o",
            primary_metric="score",
            primary_score=1.0,
            raw_result_path="/tmp/q2.json",
        ),
    ]
    warnings = detect_question_count_conflict(summary, rows)
    assert warnings and "using report value" in warnings[0]
