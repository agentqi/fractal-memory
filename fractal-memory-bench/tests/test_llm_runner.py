from __future__ import annotations

from pathlib import Path

import yaml

from benchmark_runner.reporting import load_results_json
from benchmark_runner.runner import run_benchmark


def test_runner_executes_end_to_end_llm_mode(tmp_path) -> None:
    config = {
        "mode": "end_to_end_llm",
        "approaches": ["flat_folders"],
        "task_ids": ["A1"],
        "repetitions": 1,
        "simulate_reset": True,
        "randomize_task_order": False,
        "output_directory": str(tmp_path / "outputs"),
        "corpus_directory": "benchmark_runner/corpus",
        "tasks_directory": "benchmark_runner/tasks",
        "llm": {
            "enabled": True,
            "provider": "mock",
            "model": "mock-llm",
            "temperature": 0,
            "max_tokens": 220,
        },
        "answer_scoring": {
            "enabled": True,
            "use_llm_judge": False,
            "use_rule_based_scorer": True,
        },
    }
    path = tmp_path / "pilot_llm.yaml"
    path.write_text(yaml.safe_dump(config), encoding="utf-8")

    outputs = run_benchmark(path, approach="flat_folders")
    results = load_results_json(outputs["json"])

    assert len(results.records) == 1
    record = results.records[0]
    assert record.llm_mode == "end_to_end_llm"
    assert record.llm_provider == "mock"
    assert record.llm_response is not None
    assert record.answer_score is not None
    assert record.llm_total_tokens > 0
    assert record.format_adherence_score >= 0
    assert record.llm_request is not None
    assert record.llm_request.max_tokens == 220
