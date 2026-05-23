from __future__ import annotations

from abc import ABC, abstractmethod
from pathlib import Path
from typing import Any

from benchmark_runner.config import RunConfig
from benchmark_runner.models import RetrievalItem, RetrievalResult
from benchmark_runner.token_estimation import estimate_tokens


class MemoryApproachAdapter(ABC):
    approach_name: str

    def __init__(self) -> None:
        self.corpus_path: Path | None = None
        self.run_dir: Path | None = None
        self.config: RunConfig | None = None
        self._volatile_cache: dict[str, Any] = {}

    def name(self) -> str:
        return self.approach_name

    def setup(self, corpus_path: str | Path, run_dir: str | Path, config: RunConfig) -> None:
        self.corpus_path = Path(corpus_path)
        self.run_dir = Path(run_dir)
        self.config = config
        self._volatile_cache.clear()

    def reset_context(self) -> None:
        self._volatile_cache.clear()

    @abstractmethod
    def retrieve(self, task_prompt: str, metadata: dict[str, Any]) -> RetrievalResult:
        raise NotImplementedError

    def cleanup(self) -> None:
        self._volatile_cache.clear()

    def iter_markdown_files(self) -> list[Path]:
        if self.corpus_path is None:
            raise RuntimeError("Adapter has not been set up.")
        return sorted(self.corpus_path.rglob("*.md"))

    def read_text(self, path: Path) -> str:
        return path.read_text(encoding="utf-8")

    def relative_path(self, path: Path) -> str:
        if self.corpus_path is None:
            raise RuntimeError("Adapter has not been set up.")
        return path.relative_to(self.corpus_path).as_posix()

    def build_result(
        self,
        items: list[RetrievalItem],
        raw_output: str,
        retrieval_operations: int,
        metadata: dict[str, Any] | None = None,
    ) -> RetrievalResult:
        return RetrievalResult(
            approach_name=self.name(),
            retrieved_items=items,
            files_opened=len(items),
            lines_loaded=sum(item.line_count for item in items),
            estimated_tokens_loaded=sum(item.estimated_tokens for item in items),
            retrieval_operations=retrieval_operations,
            raw_output=raw_output,
            metadata=metadata or {},
        )

    def excerpt_item(self, path: Path, content: str, score: float, relevant: bool | None = None) -> RetrievalItem:
        excerpt = content.strip()
        line_count = len([line for line in excerpt.splitlines() if line.strip()])
        return RetrievalItem(
            path=self.relative_path(path),
            title=path.stem,
            content_excerpt=excerpt,
            line_count=line_count,
            estimated_tokens=estimate_tokens(excerpt),
            relevant=relevant,
            metadata={"score": round(score, 4)},
        )


def normalize_text(value: str) -> str:
    return " ".join(value.lower().replace("_", " ").replace("-", " ").split())


def keyword_overlap_score(query: str, content: str) -> float:
    query_terms = {term for term in normalize_text(query).split() if len(term) > 2}
    if not query_terms:
        return 0.0
    haystack = normalize_text(content)
    hits = sum(1 for term in query_terms if term in haystack)
    return hits / len(query_terms)


def render_joined_items(items: list[RetrievalItem]) -> str:
    parts: list[str] = []
    for item in items:
        parts.append(f"[{item.path}]\n{item.content_excerpt}".strip())
    return "\n\n".join(parts)
