from __future__ import annotations

from benchmark_runner.models import RetrievalItem, RetrievalResult, TaskDefinition
from benchmark_runner.scoring import score_task
from benchmark_runner.token_estimation import estimate_tokens


def test_scoring_applies_raw_score_and_penalties() -> None:
    task = TaskDefinition(
        id="A1",
        category="resume-after-reset",
        project="fractal-memory-cli",
        prompt="Resume Fractal benchmark work.",
        gold_standard=[],
        must_recover=["benchmark harness", "optional wrapper", "scoring logic"],
        useful_to_recover=["CLI-first MVP", "JIT-first and AOT-ready later"],
        distractors=["GovOS authentication", "FlowOne traceability"],
        success_criteria=["CSV JSON and Markdown"],
        expected_paths=["projects/fractal-memory-cli"],
    )
    raw_output = """
    Fractal Memory CLI benchmark harness keeps MCP as an optional wrapper.
    The next step is finishing scoring logic and CSV JSON and Markdown exports.
    """
    result = RetrievalResult(
        approach_name="fractal_cli",
        retrieved_items=[
            RetrievalItem(
                path="projects/fractal-memory-cli/state.md",
                content_excerpt=raw_output,
                line_count=3,
                estimated_tokens=estimate_tokens(raw_output),
                relevant=True,
            )
        ],
        files_opened=1,
        lines_loaded=3,
        estimated_tokens_loaded=estimate_tokens(raw_output),
        retrieval_operations=1,
        raw_output=raw_output,
        metadata={},
    )

    score = score_task(task, result)

    assert score.resume_accuracy_raw >= 6
    assert score.penalty_contamination == 0
    assert score.penalty_wrong_claim == 0
    assert score.resume_accuracy_adjusted >= 6


def test_token_estimation_uses_character_divided_by_four() -> None:
    assert estimate_tokens("abcd") == 1
    assert estimate_tokens("abcdefgh") == 2
    assert estimate_tokens("") == 0
