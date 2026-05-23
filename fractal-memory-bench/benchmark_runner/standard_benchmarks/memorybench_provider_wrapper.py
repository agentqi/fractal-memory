from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from benchmark_runner.adapters.base import MemoryApproachAdapter


class MemoryBenchProviderWrapper:
    """Conceptual bridge artifact for mapping a harness adapter to a MemoryBench-style provider."""

    def __init__(self, approach_name: str, adapter: MemoryApproachAdapter) -> None:
        self.approach_name = approach_name
        self.adapter = adapter

    def build_metadata(self) -> dict[str, Any]:
        return {
            "approach_name": self.approach_name,
            "adapter_class": self.adapter.__class__.__name__,
            "provider_lifecycle": {
                "setup": "initialize / ingest",
                "retrieve": "search",
                "reset_context": "clear",
                "cleanup": "cleanup",
            },
            "corpus_path": str(self.adapter.corpus_path) if self.adapter.corpus_path else None,
            "run_dir": str(self.adapter.run_dir) if self.adapter.run_dir else None,
        }

    def write_metadata(self, output_dir: str | Path) -> Path:
        target = Path(output_dir) / "provider_wrapper.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(self.build_metadata(), indent=2), encoding="utf-8")
        return target
