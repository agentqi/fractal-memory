from __future__ import annotations

from pathlib import Path
from typing import Any

from pydantic import BaseModel, Field, field_validator


class MemoryBenchBenchmarkSpec(BaseModel):
    benchmark: str
    evaluator: str
    provider: str = "fractalmemory"
    answering_model: str | None = None
    limit: int | None = None


class MemoryBenchSettings(BaseModel):
    mode: str = "subprocess"
    executable: str = "bun"
    working_dir: str = "./vendor/memorybench"
    timeout_seconds: int = 3600
    preserve_raw_outputs: bool = True
    skip_if_unavailable: bool = True
    environment: dict[str, str] = Field(default_factory=dict)
    runs: list[MemoryBenchBenchmarkSpec] = Field(default_factory=list)


class StandardBenchmarksConfig(BaseModel):
    enabled: bool = False
    backend: str = "memorybench"
    memorybench: MemoryBenchSettings = Field(default_factory=MemoryBenchSettings)


class MemoryBenchRunConfig(BaseModel):
    mode: str = "subprocess"
    executable: str
    working_dir: Path
    benchmark: str
    evaluator: str
    provider: str = "fractalmemory"
    answering_model: str | None = None
    limit: int | None = None
    timeout_seconds: int = 3600
    preserve_raw_outputs: bool = True
    skip_if_unavailable: bool = True
    environment: dict[str, str] = Field(default_factory=dict)

    @field_validator("working_dir", mode="before")
    @classmethod
    def _coerce_working_dir(cls, value: str | Path) -> Path:
        return Path(value)


class MemoryBenchQuestionRow(BaseModel):
    run_id: str
    track: str = "standard_memorybench"
    backend: str = "memorybench"
    benchmark: str
    approach: str
    question_id: str
    evaluator: str
    primary_metric: str
    primary_score: float | None = None
    evaluation_status: str = "completed"
    latency_seconds: float | None = None
    input_tokens: int | None = None
    output_tokens: int | None = None
    total_tokens: int | None = None
    raw_result_path: str
    search_hit_count: int | None = None
    extra_metrics: dict[str, Any] = Field(default_factory=dict)


class MemoryBenchSearchHitRow(BaseModel):
    run_id: str
    track: str = "standard_memorybench"
    backend: str = "memorybench"
    benchmark: str
    approach: str
    question_id: str
    hit_rank: int
    hit_id: str
    score: float | None = None
    is_relevant: bool | None = None
    session_id: str | None = None
    source_path: str | None = None
    snippet: str | None = None
    raw_result_path: str


class MemoryBenchAggregateSummary(BaseModel):
    track: str = "standard_memorybench"
    backend: str = "memorybench"
    benchmark: str
    approach: str
    primary_metric: str
    primary_score: float | None = None
    secondary_metrics: dict[str, float] = Field(default_factory=dict)
    total_questions: int = 0
    succeeded_questions: int = 0
    failed_questions: int = 0
    raw_report_path: str
    warnings: list[str] = Field(default_factory=list)


class NormalizedMemoryBenchRun(BaseModel):
    summary: MemoryBenchAggregateSummary
    questions: list[MemoryBenchQuestionRow] = Field(default_factory=list)
    search_hits: list[MemoryBenchSearchHitRow] = Field(default_factory=list)
    warnings: list[str] = Field(default_factory=list)


class MemoryBenchAvailabilityResult(BaseModel):
    available: bool
    warnings: list[str] = Field(default_factory=list)
    errors: list[str] = Field(default_factory=list)
    executable_path: str | None = None
    working_dir: str | None = None


class MemoryBenchRunResult(BaseModel):
    status: str
    run_id: str
    benchmark: str
    approach: str
    evaluator: str
    backend: str = "memorybench"
    track: str = "standard_memorybench"
    command: list[str] = Field(default_factory=list)
    output_dir: str
    stdout_log_path: str
    stderr_log_path: str
    raw_result_files: list[str] = Field(default_factory=list)
    normalized_json_path: str | None = None
    normalized_csv_path: str | None = None
    summary_json_path: str | None = None
    summary_csv_path: str | None = None
    questions_json_path: str | None = None
    questions_csv_path: str | None = None
    search_hits_json_path: str | None = None
    search_hits_csv_path: str | None = None
    summary: MemoryBenchAggregateSummary | None = None
    warnings: list[str] = Field(default_factory=list)
    error: str | None = None
