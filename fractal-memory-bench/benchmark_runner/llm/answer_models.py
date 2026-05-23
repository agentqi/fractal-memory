from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field


class LlmRequest(BaseModel):
    system_prompt: str
    user_prompt: str
    retrieved_context: str
    answer_instructions: str
    model_name: str
    temperature: float = 0.0
    max_tokens: int = 220
    metadata: dict[str, Any] = Field(default_factory=dict)


class LlmResponse(BaseModel):
    content: str
    input_tokens: int = 0
    output_tokens: int = 0
    total_tokens: int = 0
    latency_seconds: float = 0.0
    raw_provider_response: dict[str, Any] = Field(default_factory=dict)
    cost_estimate: float = 0.0
    model_name: str = ""
    provider_name: str = ""
