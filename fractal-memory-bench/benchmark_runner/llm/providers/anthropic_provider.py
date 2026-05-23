from __future__ import annotations

from benchmark_runner.config import RunConfig
from benchmark_runner.llm.answer_models import LlmRequest, LlmResponse
from benchmark_runner.llm.base import LlmProvider


class AnthropicProvider(LlmProvider):
    def __init__(self, config: RunConfig) -> None:
        self.config = config

    def name(self) -> str:
        return "anthropic"

    def generate(self, request: LlmRequest) -> LlmResponse:
        raise NotImplementedError("Anthropic provider is not implemented yet.")
