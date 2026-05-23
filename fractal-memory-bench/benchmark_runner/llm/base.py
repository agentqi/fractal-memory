from __future__ import annotations

from abc import ABC, abstractmethod

from benchmark_runner.config import RunConfig
from benchmark_runner.llm.answer_models import LlmRequest, LlmResponse


class LlmProvider(ABC):
    @abstractmethod
    def name(self) -> str:
        raise NotImplementedError

    @abstractmethod
    def generate(self, request: LlmRequest) -> LlmResponse:
        raise NotImplementedError


def create_llm_provider(config: RunConfig) -> LlmProvider:
    provider_name = config.llm.provider
    if provider_name == "mock":
        from benchmark_runner.llm.providers.mock_provider import MockProvider

        return MockProvider()
    if provider_name == "openai":
        from benchmark_runner.llm.providers.openai_provider import OpenAiProvider

        return OpenAiProvider(config)
    if provider_name == "openai_compatible":
        from benchmark_runner.llm.providers.local_openai_compatible import LocalOpenAiCompatibleProvider

        return LocalOpenAiCompatibleProvider(config)
    if provider_name == "anthropic":
        from benchmark_runner.llm.providers.anthropic_provider import AnthropicProvider

        return AnthropicProvider(config)
    raise ValueError(f"Unknown LLM provider '{provider_name}'.")
