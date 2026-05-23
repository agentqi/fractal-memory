from __future__ import annotations

from pathlib import Path

from benchmark_runner.standard_benchmarks.memorybench_bridge import MemoryBenchBridge
from benchmark_runner.standard_benchmarks.models import MemoryBenchRunConfig


def test_memorybench_availability_reports_missing_checkout(tmp_path: Path) -> None:
    config = MemoryBenchRunConfig(
        executable="python3.11",
        working_dir=tmp_path / "missing-memorybench",
        benchmark="locomo",
        evaluator="gpt-4o",
        skip_if_unavailable=True,
    )

    result = MemoryBenchBridge().is_available(config)

    assert result.available is False
    assert any("working_dir does not exist" in error for error in result.errors)


def test_memorybench_availability_accepts_expected_repo_layout(tmp_path: Path) -> None:
    working_dir = tmp_path / "memorybench"
    working_dir.mkdir()
    (working_dir / "package.json").write_text("{}", encoding="utf-8")
    (working_dir / "benchmarks").mkdir()
    config = MemoryBenchRunConfig(
        executable="python3.11",
        working_dir=working_dir,
        benchmark="locomo",
        evaluator="gpt-4o",
    )

    result = MemoryBenchBridge().is_available(config)

    assert result.available is True
    assert result.executable_path is not None
    assert Path(result.working_dir or "") == working_dir
