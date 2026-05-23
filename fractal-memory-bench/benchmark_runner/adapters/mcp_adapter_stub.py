from __future__ import annotations

from benchmark_runner.adapters.base import MemoryApproachAdapter
from benchmark_runner.models import RetrievalResult


class McpAdapterStub(MemoryApproachAdapter):
    approach_name = "mcp_adapter_stub"

    def retrieve(self, task_prompt: str, metadata: dict) -> RetrievalResult:
        return RetrievalResult(
            approach_name=self.name(),
            retrieved_items=[],
            files_opened=0,
            lines_loaded=0,
            estimated_tokens_loaded=0,
            retrieval_operations=0,
            raw_output="",
            metadata={
                "depth_used": "mcp-stub",
                "notes": "Placeholder for future MCP-mediated evaluation.",
                "skipped": True,
            },
        )
