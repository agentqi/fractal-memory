from __future__ import annotations

import json
from pathlib import Path

import pandas as pd

from benchmark_runner.models import BenchmarkRecord
from benchmark_runner.reporting import export_markdown_report, generate_markdown_report
from benchmark_runner.standard_benchmarks.models import MemoryBenchAggregateSummary, MemoryBenchRunResult


def _custom_frame() -> pd.DataFrame:
    record = BenchmarkRecord(
        run_id="run-1",
        date="2026-04-04",
        approach="fractal_cli",
        task_id="A1",
        task_category="resume-after-reset",
        project="fractal-memory-cli",
        repetition=1,
        resume_accuracy_raw=8,
        penalty_irrelevant_context=0,
        penalty_contamination=0,
        penalty_wrong_claim=0,
        resume_accuracy_adjusted=8,
        files_opened=2,
        lines_loaded=20,
        tokens_loaded_est=120,
        retrieval_operations=1,
        depth_used="branch-selective",
        resume_time_seconds=0.2,
        retrieval_precision=1.0,
        retrieval_recall=1.0,
        branch_purity=1.0,
        notes="",
        raw_output="",
        retrieved_paths=[],
        adapter_metadata={},
    )
    return pd.DataFrame([record.model_dump()])


def _standard_result(tmp_path: Path, *, status: str = "succeeded", warnings: list[str] | None = None) -> MemoryBenchRunResult:
    questions_path = tmp_path / "questions.json"
    search_hits_path = tmp_path / "search_hits.json"
    questions_path.write_text(json.dumps([{"latency_seconds": 95.058}]), encoding="utf-8")
    search_hits_path.write_text(json.dumps([{"hit_rank": 1}, {"hit_rank": 2}]), encoding="utf-8")
    return MemoryBenchRunResult(
        status=status,
        run_id="run-1",
        benchmark="locomo",
        approach="fractal_cli",
        evaluator="gpt-4o",
        output_dir=str(tmp_path),
        stdout_log_path=str(tmp_path / "stdout.log"),
        stderr_log_path=str(tmp_path / "stderr.log"),
        questions_json_path=str(questions_path),
        search_hits_json_path=str(search_hits_path),
        summary=MemoryBenchAggregateSummary(
            benchmark="locomo",
            approach="fractal_cli",
            primary_metric="accuracy",
            primary_score=1.0,
            total_questions=1,
            succeeded_questions=1,
            failed_questions=0,
            raw_report_path=str(tmp_path / "report.json"),
            warnings=warnings or [],
        ),
        warnings=warnings or [],
        error=None if status == "succeeded" else "provider unavailable",
    )


def test_generate_report_for_custom_only_run() -> None:
    report = generate_markdown_report(_custom_frame(), title="Custom Only")

    assert "Custom benchmark rows: 1" in report
    assert "Standard benchmark runs: 0" in report
    assert "## Custom Benchmarks" in report
    assert "## Standard Benchmarks" not in report


def test_generate_report_for_standard_only_run(tmp_path: Path) -> None:
    report = generate_markdown_report(
        pd.DataFrame(),
        title="Standard Only",
        standard_results=[_standard_result(tmp_path)],
    )

    assert "Custom benchmark rows: 0" in report
    assert "Successful standard runs: 1" in report
    assert "## Standard Benchmarks" in report
    assert "locomo" in report
    assert "Rows: 0" not in report


def test_generate_report_for_mixed_run(tmp_path: Path) -> None:
    report = generate_markdown_report(
        _custom_frame(),
        title="Mixed",
        standard_results=[_standard_result(tmp_path)],
    )

    assert "## Custom Benchmarks" in report
    assert "## Standard Benchmarks" in report
    assert "## Standard Benchmark Details" in report


def test_generate_report_includes_standard_warnings(tmp_path: Path) -> None:
    report = generate_markdown_report(
        pd.DataFrame(),
        title="Warnings",
        standard_results=[_standard_result(tmp_path, warnings=["cache miss"])],
    )

    assert "cache miss" in report


def test_generate_report_for_skipped_standard_backend(tmp_path: Path) -> None:
    report = generate_markdown_report(
        pd.DataFrame(),
        title="Skipped",
        standard_results=[_standard_result(tmp_path, status="skipped_unavailable")],
    )

    assert "skipped_unavailable" in report


def test_export_markdown_report_loads_standard_manifest(tmp_path: Path) -> None:
    manifest_path = tmp_path / "standard_benchmarks" / "memorybench" / "manifest.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(
        json.dumps([_standard_result(tmp_path).model_dump(mode="json")], indent=2),
        encoding="utf-8",
    )
    report_path = tmp_path / "report.md"

    export_markdown_report(pd.DataFrame(), report_path, standard_manifest_path=manifest_path)

    content = report_path.read_text(encoding="utf-8")
    assert "## Standard Benchmarks" in content
    assert "Successful standard runs: 1" in content
