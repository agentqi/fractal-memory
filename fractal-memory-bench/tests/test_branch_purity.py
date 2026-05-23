from __future__ import annotations

from benchmark_runner.models import RetrievalItem, RetrievalResult, TaskDefinition
from benchmark_runner.scoring import compute_branch_purity


def test_branch_purity_penalizes_cross_project_retrieval() -> None:
    task = TaskDefinition(
        id="D1",
        category="cross-project-separation",
        project="govos",
        prompt="Switch cleanly to GovOS.",
        gold_standard=[],
        must_recover=["endpoint planning"],
        useful_to_recover=[],
        distractors=["lineage sidebar"],
        success_criteria=[],
        expected_paths=["projects/govos"],
    )
    result = RetrievalResult(
        approach_name="flat_folders",
        raw_output="",
        files_opened=2,
        lines_loaded=10,
        estimated_tokens_loaded=80,
        retrieval_operations=1,
        retrieved_items=[
            RetrievalItem(path="projects/govos/state.md", content_excerpt="", relevant=True),
            RetrievalItem(path="projects/flowone/state.md", content_excerpt="", relevant=False),
        ],
        metadata={},
    )

    assert compute_branch_purity(task, result) == 0.5
