from __future__ import annotations

import json
import os
import urllib.request
from time import perf_counter

from benchmark_runner.config import RunConfig
from benchmark_runner.llm.answer_models import LlmRequest, LlmResponse
from benchmark_runner.llm.base import LlmProvider
from benchmark_runner.llm.cost_estimation import estimate_cost
from benchmark_runner.token_estimation import estimate_tokens


class OpenAiProvider(LlmProvider):
    def __init__(self, config: RunConfig) -> None:
        self.config = config

    def name(self) -> str:
        return "openai"

    def generate(self, request: LlmRequest) -> LlmResponse:
        provider_config = self.config.providers.get("openai")
        api_key_env = provider_config.api_key_env if provider_config and provider_config.api_key_env else "OPENAI_API_KEY"
        api_key = os.environ.get(api_key_env)
        if not api_key:
            raise RuntimeError(f"Environment variable '{api_key_env}' is not set.")

        payload = {
            "model": request.model_name,
            "input": (
                f"[SYSTEM]\n{request.system_prompt}\n\n"
                f"[RETRIEVED MEMORY]\n{request.retrieved_context}\n\n"
                f"[USER TASK]\n{request.user_prompt}\n\n"
                f"[ANSWER INSTRUCTIONS]\n{request.answer_instructions}"
            ),
            "temperature": request.temperature,
            "max_output_tokens": request.max_tokens,
        }
        http_request = urllib.request.Request(
            url="https://api.openai.com/v1/responses",
            data=json.dumps(payload).encode("utf-8"),
            headers={
                "Authorization": f"Bearer {api_key}",
                "Content-Type": "application/json",
            },
            method="POST",
        )
        start = perf_counter()
        with urllib.request.urlopen(http_request, timeout=self.config.llm.timeout_seconds) as response:
            raw = json.loads(response.read().decode("utf-8"))
        latency = perf_counter() - start
        content = _extract_output_text(raw)
        input_tokens = int(raw.get("usage", {}).get("input_tokens", estimate_tokens(payload["input"])))
        output_tokens = int(raw.get("usage", {}).get("output_tokens", estimate_tokens(content)))
        return LlmResponse(
            content=content,
            input_tokens=input_tokens,
            output_tokens=output_tokens,
            total_tokens=input_tokens + output_tokens,
            latency_seconds=round(latency, 6),
            raw_provider_response=raw,
            cost_estimate=estimate_cost(self.name(), request.model_name, input_tokens, output_tokens),
            model_name=request.model_name,
            provider_name=self.name(),
        )


def _extract_output_text(raw: dict) -> str:
    parts: list[str] = []
    for item in raw.get("output", []):
        for content in item.get("content", []):
            text = content.get("text")
            if text:
                parts.append(text)
    return "\n".join(parts).strip()
