from __future__ import annotations

import pandas as pd

from benchmark_runner.models import BenchmarkRecord
from benchmark_runner.reporting import CSV_COLUMNS, export_csv


def test_csv_export_contains_expected_columns(tmp_path) -> None:
    record = BenchmarkRecord(
        run_id="run-1",
        date="2026-04-03",
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
    csv_path = export_csv([record], tmp_path / "results.csv")
    frame = pd.read_csv(csv_path)

    assert list(frame.columns) == CSV_COLUMNS
    assert frame.iloc[0]["approach"] == "fractal_cli"
    assert "format_adherence_score" in frame.columns
