from __future__ import annotations

from time import perf_counter
from typing import Any

from benchmark_runner.adapters.base import MemoryApproachAdapter
from benchmark_runner.models import RetrievalResult


def simulate_reset(
    adapter: MemoryApproachAdapter,
    task_prompt: str,
    metadata: dict[str, Any],
) -> tuple[RetrievalResult, float]:
    midpoint = max(1, len(task_prompt) // 2)
    primer_prompt = task_prompt[:midpoint].strip()
    if primer_prompt:
        adapter.retrieve(primer_prompt, metadata)

    adapter.reset_context()

    start = perf_counter()
    result = adapter.retrieve(task_prompt, metadata)
    duration = perf_counter() - start
    result.metadata["reset_simulated"] = True
    return result, duration
