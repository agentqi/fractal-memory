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


class LocalOpenAiCompatibleProvider(LlmProvider):
    def __init__(self, config: RunConfig) -> None:
        self.config = config

    def name(self) -> str:
        return "openai_compatible"

    def generate(self, request: LlmRequest) -> LlmResponse:
        provider_config = self.config.providers.get("openai_compatible")
        if provider_config is None or not provider_config.base_url_env:
            raise RuntimeError("OpenAI-compatible provider config is missing base_url_env.")
        base_url = os.environ.get(provider_config.base_url_env)
        if not base_url:
            raise RuntimeError(f"Environment variable '{provider_config.base_url_env}' is not set.")
        api_key = os.environ.get(provider_config.api_key_env, "") if provider_config.api_key_env else ""

        payload = {
            "model": request.model_name,
            "messages": [
                {"role": "system", "content": request.system_prompt},
                {
                    "role": "user",
                    "content": (
                        f"[RETRIEVED MEMORY]\n{request.retrieved_context}\n\n"
                        f"[USER TASK]\n{request.user_prompt}\n\n"
                        f"[ANSWER INSTRUCTIONS]\n{request.answer_instructions}"
                    ),
                },
            ],
            "temperature": request.temperature,
            "max_tokens": request.max_tokens,
        }
        headers = {"Content-Type": "application/json"}
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"
        http_request = urllib.request.Request(
            url=f"{base_url.rstrip('/')}/chat/completions",
            data=json.dumps(payload).encode("utf-8"),
            headers=headers,
            method="POST",
        )
        start = perf_counter()
        with urllib.request.urlopen(http_request, timeout=self.config.llm.timeout_seconds) as response:
            raw = json.loads(response.read().decode("utf-8"))
        latency = perf_counter() - start
        choice = ((raw.get("choices") or [{}])[0]).get("message", {})
        content = str(choice.get("content", "")).strip()
        usage = raw.get("usage", {})
        input_tokens = int(usage.get("prompt_tokens", estimate_tokens(request.system_prompt + request.user_prompt + request.retrieved_context)))
        output_tokens = int(usage.get("completion_tokens", estimate_tokens(content)))
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
