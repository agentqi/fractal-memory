from __future__ import annotations

from benchmark_runner.adapters.base import MemoryApproachAdapter
from benchmark_runner.config import RunConfig
from benchmark_runner.models import RetrievalResult
from benchmark_runner.reset_simulator import simulate_reset


class DummyAdapter(MemoryApproachAdapter):
    approach_name = "dummy"

    def retrieve(self, task_prompt: str, metadata: dict) -> RetrievalResult:
        seen = int(self._volatile_cache.get("seen", 0)) + 1
        self._volatile_cache["seen"] = seen
        return RetrievalResult(
            approach_name=self.name(),
            raw_output=f"{task_prompt} seen={seen}",
            files_opened=0,
            lines_loaded=0,
            estimated_tokens_loaded=0,
            retrieval_operations=1,
            metadata={"depth_used": "dummy"},
        )


def test_reset_simulator_clears_volatile_context(tmp_path) -> None:
    adapter = DummyAdapter()
    adapter.setup(tmp_path, tmp_path / "run", RunConfig())

    result, duration = simulate_reset(adapter, "resume this task", {"project": "demo"})

    assert "seen=1" in result.raw_output
    assert duration >= 0
    assert result.metadata["reset_simulated"] is True
