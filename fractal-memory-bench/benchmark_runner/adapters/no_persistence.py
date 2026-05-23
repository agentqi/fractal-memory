from __future__ import annotations

from benchmark_runner.adapters.base import MemoryApproachAdapter


class NoPersistenceAdapter(MemoryApproachAdapter):
    approach_name = "no_persistence"

    def retrieve(self, task_prompt: str, metadata: dict) -> "RetrievalResult":
        from benchmark_runner.models import RetrievalResult

        return RetrievalResult(
            approach_name=self.name(),
            retrieved_items=[],
            files_opened=0,
            lines_loaded=0,
            estimated_tokens_loaded=0,
            retrieval_operations=0,
            raw_output="",
            metadata={"depth_used": "none", "notes": "No durable retrieval available."},
        )
