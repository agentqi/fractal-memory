from __future__ import annotations

from benchmark_runner.llm.answer_models import LlmRequest
from benchmark_runner.llm.providers.mock_provider import MockProvider


def test_mock_provider_returns_deterministic_response() -> None:
    provider = MockProvider()
    request = LlmRequest(
        system_prompt="system",
        user_prompt="user",
        retrieved_context="line one\nline two",
        answer_instructions="answer",
        model_name="mock-llm",
    )

    response = provider.generate(request)

    assert response.provider_name == "mock"
    assert "line one" in response.content
    assert response.total_tokens >= response.output_tokens
