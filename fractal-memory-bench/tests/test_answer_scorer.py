from __future__ import annotations

from benchmark_runner.evaluators.answer_scorer import RuleBasedAnswerScorer
from benchmark_runner.llm.answer_models import LlmResponse
from benchmark_runner.models import RetrievalResult, TaskDefinition


def test_rule_based_answer_scorer_scores_expected_phrases() -> None:
    task = TaskDefinition(
        id="C1",
        category="handoff-resume",
        project="fractal-memory-cli",
        prompt="Create and resume from the latest Fractal handoff.",
        gold_standard=[],
        must_recover=["latest fractal handoff", "benchmark harness scaffold", "regression tests"],
        success_criteria=["scoring"],
        answer_rubric={
            "expected_phrases": ["latest fractal handoff", "benchmark harness scaffold", "regression tests"],
            "forbidden_phrases": ["GovOS authentication"],
            "unsupported_claim_patterns": ["already finalized.*"],
        },
    )
    retrieval = RetrievalResult(approach_name="fractal_cli", raw_output="latest fractal handoff benchmark harness scaffold regression tests")
    response = LlmResponse(
        content=(
            "1. Project / Branch\nFractal Memory CLI\n"
            "2. Current Objective\nResume from the latest fractal handoff and finish the benchmark harness scaffold.\n"
            "3. Key Prior Decision\nRegression tests are still required.\n"
            "4. Active Constraint\nStay local-first and deterministic.\n"
            "5. Next Best Action\nFinish scoring and regression tests.\n"
            "6. Missing Information\nNot supported by retrieved memory."
        )
    )

    score = RuleBasedAnswerScorer().score(task, response, retrieval)

    assert score.final_answer_score_adjusted >= 6
    assert score.answer_recall > 0
    assert score.contamination_penalty == 0
    assert score.format_adherence_score == 2
