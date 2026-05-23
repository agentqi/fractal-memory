from __future__ import annotations

from pathlib import Path

import yaml

from benchmark_runner.config import expand_memorybench_runs, load_config


def test_memorybench_config_expands_runs(tmp_path: Path) -> None:
    working_dir = tmp_path / "vendor" / "memorybench"
    config_payload = {
        "approaches": ["fractal_cli"],
        "task_ids": ["A1"],
        "standard_benchmarks": {
            "enabled": True,
            "backend": "memorybench",
            "memorybench": {
                "mode": "subprocess",
                "executable": "bun",
                "working_dir": str(working_dir),
                "timeout_seconds": 120,
                "preserve_raw_outputs": True,
                "skip_if_unavailable": True,
                "environment": {"OPENAI_API_KEY": "${OPENAI_API_KEY}"},
                "runs": [
                    {"benchmark": "locomo", "evaluator": "gpt-4o", "provider": "fractalmemory", "answering_model": "gpt-4o", "limit": 5},
                    {"benchmark": "longmemeval", "evaluator": "gpt-4o", "provider": "fractalmemory", "answering_model": "gpt-4o"},
                ],
            },
        },
    }
    path = tmp_path / "config.yaml"
    path.write_text(yaml.safe_dump(config_payload), encoding="utf-8")

    config = load_config(path)
    runs = expand_memorybench_runs(config, repo_root=tmp_path)

    assert len(runs) == 2
    assert runs[0].benchmark == "locomo"
    assert runs[1].benchmark == "longmemeval"
    assert runs[0].evaluator == "gpt-4o"
    assert runs[0].working_dir == working_dir
    assert runs[0].provider == "fractalmemory"
    assert runs[0].answering_model == "gpt-4o"
    assert runs[0].limit == 5
