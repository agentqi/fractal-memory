from __future__ import annotations

import json
import subprocess
from pathlib import Path

from benchmark_runner.adapters.no_persistence import NoPersistenceAdapter
from benchmark_runner.config import RunConfig
from benchmark_runner.standard_benchmarks.memorybench_bridge import MemoryBenchBridge
from benchmark_runner.standard_benchmarks.models import MemoryBenchRunConfig


def _make_memorybench_checkout(tmp_path: Path) -> Path:
    working_dir = tmp_path / "memorybench"
    working_dir.mkdir()
    (working_dir / "package.json").write_text("{}", encoding="utf-8")
    (working_dir / "benchmarks").mkdir()
    return working_dir


def _make_adapter(tmp_path: Path) -> NoPersistenceAdapter:
    corpus = tmp_path / "corpus"
    corpus.mkdir()
    adapter = NoPersistenceAdapter()
    adapter.setup(corpus, tmp_path / "run" / "adapter", RunConfig())
    return adapter


def test_memorybench_bridge_builds_command_and_writes_outputs(tmp_path: Path, monkeypatch) -> None:
    working_dir = _make_memorybench_checkout(tmp_path)
    run_output_root = tmp_path / "outputs" / "run-123" / "standard_benchmarks" / "memorybench"
    adapter = _make_adapter(tmp_path)
    bridge = MemoryBenchBridge()
    config = MemoryBenchRunConfig(
        executable="python3.11",
        working_dir=working_dir,
        benchmark="locomo",
        evaluator="gpt-4o",
        provider="fractalmemory",
        answering_model="gpt-4o",
        limit=3,
        environment={"OPENAI_API_KEY": "${OPENAI_API_KEY}"},
    )
    monkeypatch.setenv("OPENAI_API_KEY", "test-key")

    calls: list[list[str]] = []

    def fake_run(cmd, cwd=None, env=None, capture_output=None, text=None, timeout=None, check=None):
        calls.append(cmd)
        if cmd[-1] == "--version":
            return subprocess.CompletedProcess(cmd, 0, stdout="Python 3.11\n", stderr="")
        raw_dir = working_dir / "data" / "runs" / "run-123"
        raw_dir.mkdir(parents=True, exist_ok=True)
        report_payload = {
            "runId": "run-123",
            "summary": {"totalQuestions": 1, "correctCount": 1, "accuracy": 1.0},
            "evaluations": [
                {
                    "questionId": "q1",
                    "questionType": "single-hop",
                    "score": 1,
                    "label": "correct",
                    "totalDurationMs": 400,
                }
            ],
        }
        question_payload = {
            "questionId": "q1",
            "results": [{"sessionId": "s1", "score": 1.0, "timeline_excerpt": "alpha"}],
        }
        results_dir = raw_dir / "results"
        results_dir.mkdir(exist_ok=True)
        (raw_dir / "report.json").write_text(json.dumps(report_payload), encoding="utf-8")
        (results_dir / "q1.json").write_text(json.dumps(question_payload), encoding="utf-8")
        return subprocess.CompletedProcess(cmd, 0, stdout="ok", stderr="")

    monkeypatch.setattr(subprocess, "run", fake_run)

    result = bridge.run("fractal_cli", adapter, config, str(run_output_root))

    assert result.status == "succeeded"
    assert result.command[1:4] == ["run", "src/index.ts", "run"]
    assert result.command[0].endswith("python3.11")
    assert "-p" in result.command and "fractalmemory" in result.command
    assert "-l" in result.command and "3" in result.command
    assert result.normalized_json_path is not None
    assert Path(result.normalized_json_path).exists()
    assert result.questions_json_path is not None
    assert result.search_hits_json_path is not None
    assert result.summary_json_path is not None
    assert Path(result.stdout_log_path).exists()
    assert any("--provider-manifest" in part for part in result.command)
    assert len(calls) == 2


def test_memorybench_bridge_skips_when_unavailable(tmp_path: Path) -> None:
    adapter = _make_adapter(tmp_path)
    bridge = MemoryBenchBridge()
    config = MemoryBenchRunConfig(
        executable="missing-executable",
        working_dir=tmp_path / "missing-memorybench",
        benchmark="locomo",
        evaluator="gpt-4o",
        provider="fractalmemory",
        skip_if_unavailable=True,
    )

    result = bridge.run("fractal_cli", adapter, config, str(tmp_path / "outputs" / "run-123" / "standard_benchmarks" / "memorybench"))

    assert result.status == "skipped_unavailable"
    assert result.error is None
    assert result.warnings


def test_memorybench_bridge_fails_when_outputs_missing(tmp_path: Path, monkeypatch) -> None:
    working_dir = _make_memorybench_checkout(tmp_path)
    adapter = _make_adapter(tmp_path)
    bridge = MemoryBenchBridge()
    config = MemoryBenchRunConfig(
        executable="python3.11",
        working_dir=working_dir,
        benchmark="locomo",
        evaluator="gpt-4o",
        provider="fractalmemory",
    )

    def fake_run(cmd, cwd=None, env=None, capture_output=None, text=None, timeout=None, check=None):
        if cmd[-1] == "--version":
            return subprocess.CompletedProcess(cmd, 0, stdout="Python 3.11\n", stderr="")
        return subprocess.CompletedProcess(cmd, 0, stdout="ok", stderr="")

    monkeypatch.setattr(subprocess, "run", fake_run)

    result = bridge.run("flat_folders", adapter, config, str(tmp_path / "outputs" / "run-123" / "standard_benchmarks" / "memorybench"))

    assert result.status == "failed"
    assert result.error is not None
    assert "did not produce report.json" in result.error


def test_memorybench_bridge_skips_misconfigured_when_env_placeholder_unset(tmp_path: Path) -> None:
    working_dir = _make_memorybench_checkout(tmp_path)
    adapter = _make_adapter(tmp_path)
    bridge = MemoryBenchBridge()
    config = MemoryBenchRunConfig(
        executable="python3.11",
        working_dir=working_dir,
        benchmark="locomo",
        evaluator="gpt-4o",
        provider="fractalmemory",
        skip_if_unavailable=True,
        environment={"OPENAI_API_KEY": "${OPENAI_API_KEY}"},
    )

    result = bridge.run("fractal_cli", adapter, config, str(tmp_path / "outputs" / "run-123" / "standard_benchmarks" / "memorybench"))

    assert result.status == "skipped_misconfigured"
    assert result.error is not None
    assert "OPENAI_API_KEY" in result.error
