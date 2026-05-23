from __future__ import annotations

from datetime import datetime
from pathlib import Path
from typing import Any

from pydantic import BaseModel, Field


class AnswerRubric(BaseModel):
    expected_phrases: list[str] = Field(default_factory=list)
    forbidden_phrases: list[str] = Field(default_factory=list)
    unsupported_claim_patterns: list[str] = Field(default_factory=list)


class ShortAnswerTarget(BaseModel):
    project_branch: str | None = None
    current_objective: str | None = None
    key_prior_decision: str | None = None
    active_constraint: str | None = None
    next_best_action: str | None = None
    missing_information: str | None = None


class TaskDefinition(BaseModel):
    id: str
    category: str
    project: str
    prompt: str
    gold_standard: list[str]
    must_recover: list[str]
    useful_to_recover: list[str] = Field(default_factory=list)
    distractors: list[str] = Field(default_factory=list)
    success_criteria: list[str] = Field(default_factory=list)
    expected_paths: list[str] = Field(default_factory=list)
    answer_rubric: AnswerRubric = Field(default_factory=AnswerRubric)
    short_answer_target: ShortAnswerTarget | None = None
    notes: str | None = None


class RetrievalItem(BaseModel):
    path: str
    title: str | None = None
    content_excerpt: str
    line_count: int = 0
    estimated_tokens: int = 0
    relevant: bool | None = None
    metadata: dict[str, Any] = Field(default_factory=dict)


class RetrievalResult(BaseModel):
    approach_name: str
    retrieved_items: list[RetrievalItem] = Field(default_factory=list)
    files_opened: int = 0
    lines_loaded: int = 0
    estimated_tokens_loaded: int = 0
    retrieval_operations: int = 0
    raw_output: str = ""
    metadata: dict[str, Any] = Field(default_factory=dict)


class ScoreBreakdown(BaseModel):
    resume_accuracy_raw: int
    penalty_irrelevant_context: int
    penalty_contamination: int
    penalty_wrong_claim: int
    resume_accuracy_adjusted: int
    retrieval_precision: float
    retrieval_recall: float
    branch_purity: float
    trust_score: float | None = None
    frustration_score: float | None = None
    cognitive_load_score: float | None = None
    helpfulness_score: float | None = None
    confidence_score: float | None = None
    notes: str = ""


class LlmRequestRecord(BaseModel):
    system_prompt: str
    user_prompt: str
    retrieved_context: str
    answer_instructions: str
    model_name: str
    temperature: float
    max_tokens: int
    metadata: dict[str, Any] = Field(default_factory=dict)


class LlmResponseRecord(BaseModel):
    content: str
    input_tokens: int = 0
    output_tokens: int = 0
    total_tokens: int = 0
    latency_seconds: float = 0.0
    raw_provider_response: dict[str, Any] = Field(default_factory=dict)
    cost_estimate: float = 0.0
    model_name: str = ""
    provider_name: str = ""


class AnswerScore(BaseModel):
    final_answer_score_raw: int = 0
    contamination_penalty: int = 0
    hallucinated_continuity_penalty: int = 0
    unsupported_claim_penalty: int = 0
    verbosity_penalty: int = 0
    format_adherence_score: int = 0
    final_answer_score_adjusted: int = 0
    answer_precision: float = 0.0
    answer_recall: float = 0.0
    task_completion_score: int = 0
    continuity_confidence_score: int = 0
    notes: str = ""


class BenchmarkRecord(BaseModel):
    run_id: str
    date: str
    approach: str
    task_id: str
    task_category: str
    project: str
    repetition: int
    resume_accuracy_raw: int
    penalty_irrelevant_context: int
    penalty_contamination: int
    penalty_wrong_claim: int
    resume_accuracy_adjusted: int
    files_opened: int
    lines_loaded: int
    tokens_loaded_est: int
    retrieval_operations: int
    depth_used: str
    resume_time_seconds: float
    retrieval_precision: float
    retrieval_recall: float
    branch_purity: float
    trust_score: float | None = None
    frustration_score: float | None = None
    cognitive_load_score: float | None = None
    helpfulness_score: float | None = None
    confidence_score: float | None = None
    llm_mode: str = "retrieval_only"
    llm_provider: str | None = None
    llm_model: str | None = None
    llm_input_tokens: int = 0
    llm_output_tokens: int = 0
    llm_total_tokens: int = 0
    llm_latency_seconds: float = 0.0
    llm_cost_estimate: float = 0.0
    final_answer_score_raw: int = 0
    final_answer_score_adjusted: int = 0
    format_adherence_score: int = 0
    answer_precision: float = 0.0
    answer_recall: float = 0.0
    task_completion_score: int = 0
    answer_contamination_penalty: int = 0
    hallucinated_continuity_penalty: int = 0
    unsupported_claim_penalty: int = 0
    llm_response_text: str | None = None
    notes: str = ""
    raw_output: str = ""
    retrieved_paths: list[str] = Field(default_factory=list)
    adapter_metadata: dict[str, Any] = Field(default_factory=dict)
    llm_request: LlmRequestRecord | None = None
    llm_response: LlmResponseRecord | None = None
    answer_score: AnswerScore | None = None


class RunManifest(BaseModel):
    run_id: str
    created_at: datetime
    config_name: str
    approaches: list[str]
    task_ids: list[str]
    mode: str = "retrieval_only"
    simulate_reset: bool
    repetitions: int
    output_directory: str


class RunResults(BaseModel):
    manifest: RunManifest
    records: list[BenchmarkRecord]


class CorpusValidationResult(BaseModel):
    valid: bool
    issues: list[str] = Field(default_factory=list)
    files_checked: int = 0


class LoadedBenchmarkContext(BaseModel):
    repo_root: Path
    corpus_path: Path
    tasks_path: Path
