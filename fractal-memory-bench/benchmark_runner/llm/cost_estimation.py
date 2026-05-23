from __future__ import annotations


def estimate_cost(provider_name: str, model_name: str, input_tokens: int, output_tokens: int) -> float:
    if provider_name == "mock":
        return 0.0
    # Deliberately conservative placeholder until provider-specific pricing is configured.
    return round(((input_tokens + output_tokens) / 1_000_000) * 1.0, 6)
