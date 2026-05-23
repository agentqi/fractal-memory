from __future__ import annotations

from benchmark_runner.config import resolve_repo_root
from benchmark_runner.runner import initialize_corpus, load_tasks, validate_corpus


def test_task_loading_and_validation() -> None:
    repo_root = resolve_repo_root()
    tasks = load_tasks(repo_root / "benchmark_runner" / "tasks")

    assert len(tasks) == 12
    assert tasks[0].id == "A1"
    assert any(task.id == "D2" for task in tasks)


def test_initialize_corpus_copies_bundled_files(tmp_path) -> None:
    target = tmp_path / "corpus-copy"
    initialize_corpus(target)

    assert (target / "projects" / "fractal-memory-cli" / "index.md").exists()
    assert (target / "handoffs" / "latest-govos.md").exists()


def test_validate_corpus_passes_for_bundled_assets() -> None:
    repo_root = resolve_repo_root()
    result = validate_corpus(repo_root / "benchmark_runner" / "corpus", repo_root / "benchmark_runner" / "tasks")

    assert result.valid is True
    assert result.files_checked >= 20
