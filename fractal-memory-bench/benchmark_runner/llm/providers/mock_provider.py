from __future__ import annotations

from time import perf_counter

from benchmark_runner.llm.answer_models import LlmRequest, LlmResponse
from benchmark_runner.llm.base import LlmProvider
from benchmark_runner.token_estimation import estimate_tokens


class MockProvider(LlmProvider):
    def name(self) -> str:
        return "mock"

    def generate(self, request: LlmRequest) -> LlmResponse:
        start = perf_counter()
        context = request.retrieved_context or ""
        lines = [line.strip() for line in context.splitlines() if line.strip()][:10]
        content = "\n".join(lines) if lines else "Missing retrieved memory."
        latency = perf_counter() - start
        input_tokens = estimate_tokens(request.system_prompt + request.user_prompt + request.retrieved_context + request.answer_instructions)
        output_tokens = estimate_tokens(content)
        return LlmResponse(
            content=content,
            input_tokens=input_tokens,
            output_tokens=output_tokens,
            total_tokens=input_tokens + output_tokens,
            latency_seconds=round(latency, 6),
            raw_provider_response={"mock": True},
            cost_estimate=0.0,
            model_name=request.model_name,
            provider_name=self.name(),
        )
