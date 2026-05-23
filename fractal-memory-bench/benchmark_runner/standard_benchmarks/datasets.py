from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class SupportedDataset:
    canonical_name: str
    aliases: tuple[str, ...]
    description: str


SUPPORTED_DATASETS: dict[str, SupportedDataset] = {
    "locomo": SupportedDataset(
        canonical_name="locomo",
        aliases=("locomo", "lo-co-mo"),
        description="Long conversational memory benchmark.",
    ),
    "longmemeval": SupportedDataset(
        canonical_name="longmemeval",
        aliases=("longmemeval", "long-mem-eval", "longmem"),
        description="Long context memory evaluation benchmark.",
    ),
}


def normalize_benchmark_name(value: str) -> str:
    normalized = value.strip().lower().replace("_", "").replace("-", "")
    for dataset in SUPPORTED_DATASETS.values():
        if normalized in {alias.replace("-", "").replace("_", "") for alias in dataset.aliases}:
            return dataset.canonical_name
    raise ValueError(
        f"Unsupported MemoryBench benchmark '{value}'. Supported: {', '.join(sorted(SUPPORTED_DATASETS))}"
    )


def is_supported_benchmark(value: str) -> bool:
    try:
        normalize_benchmark_name(value)
    except ValueError:
        return False
    return True
