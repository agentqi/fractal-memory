from __future__ import annotations

import importlib.util

from benchmark_runner.adapters.base import MemoryApproachAdapter
from benchmark_runner.models import RetrievalResult


class MemoryBenchAdapter(MemoryApproachAdapter):
    approach_name = "memorybench_adapter"

    def retrieve(self, task_prompt: str, metadata: dict) -> RetrievalResult:
        installed = importlib.util.find_spec("memorybench") is not None
        notes = (
            "MemoryBench detected. Replace this shell with a concrete runner binding."
            if installed
            else "MemoryBench not installed; adapter skipped."
        )
        return RetrievalResult(
            approach_name=self.name(),
            retrieved_items=[],
            files_opened=0,
            lines_loaded=0,
            estimated_tokens_loaded=0,
            retrieval_operations=0,
            raw_output="",
            metadata={"depth_used": "external", "notes": notes, "skipped": not installed},
        )
