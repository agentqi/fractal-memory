from __future__ import annotations

from pathlib import Path

import yaml

from benchmark_runner.config import resolve_repo_root
from benchmark_runner.reporting import load_results_json
from benchmark_runner.runner import run_benchmark


def write_config(tmp_path: Path, approaches: list[str], task_ids: list[str]) -> Path:
    repo_root = resolve_repo_root()
    config = {
        "approaches": approaches,
        "task_ids": task_ids,
        "repetitions": 1,
        "simulate_reset": True,
        "randomize_task_order": False,
        "output_directory": str(tmp_path / "outputs"),
        "corpus_directory": str(repo_root / "benchmark_runner" / "corpus"),
        "tasks_directory": str(repo_root / "benchmark_runner" / "tasks"),
        "use_memorybench_if_available": False,
        "max_files_per_retrieval": 4,
    }
    path = tmp_path / "config.yaml"
    path.write_text(yaml.safe_dump(config), encoding="utf-8")
    return path


def test_runner_executes_single_approach(tmp_path) -> None:
    config_path = write_config(tmp_path, ["fractal_cli"], ["A1"])
    outputs = run_benchmark(config_path, approach="fractal_cli")

    assert outputs["json"].exists()
    assert outputs["csv"].exists()
    assert outputs["report"].exists()

    results = load_results_json(outputs["json"])
    assert len(results.records) == 1
    assert results.records[0].approach == "fractal_cli"


def test_runner_executes_multiple_approaches(tmp_path) -> None:
    config_path = write_config(tmp_path, ["fractal_cli", "flat_folders"], ["A1", "B2"])
    outputs = run_benchmark(config_path, approach="all")
    results = load_results_json(outputs["json"])

    assert len(results.records) == 4
    assert {record.approach for record in results.records} == {"fractal_cli", "flat_folders"}
