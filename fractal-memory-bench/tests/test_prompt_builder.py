from __future__ import annotations

from benchmark_runner.llm.prompt_builder import build_prompt, dry_run_prompt
from benchmark_runner.models import RetrievalResult, TaskDefinition


def test_prompt_builder_uses_stable_sections() -> None:
    task = TaskDefinition(
        id="A1",
        category="resume-after-reset",
        project="fractal-memory-cli",
        prompt="Resume work.",
        gold_standard=[],
        must_recover=["benchmark harness"],
        short_answer_target={
            "project_branch": "Fractal Memory CLI",
            "current_objective": "Finish the benchmark harness.",
            "key_prior_decision": "MCP stays optional.",
            "active_constraint": "Keep core logic independent.",
            "next_best_action": "Finish scoring.",
            "missing_information": "Anything not in memory.",
        },
    )
    retrieval = RetrievalResult(approach_name="fractal_cli", raw_output="[projects/x]\ncontent")
    request = build_prompt(task, retrieval, "mock-llm", 0, 220)
    rendered = dry_run_prompt(request)

    assert "[SYSTEM]" in rendered
    assert "[RETRIEVED MEMORY]" in rendered
    assert "[USER TASK]" in rendered
    assert "[ANSWER INSTRUCTIONS]" in rendered
    assert "Resume work." in rendered
    assert "1. Project / Branch" in rendered
    assert "6. Missing Information" in rendered
    assert "Keep the answer under 180 words." in rendered
    assert "Category guidance:" in rendered
    assert "Reference short-form target:" in rendered
    assert request.max_tokens == 220
