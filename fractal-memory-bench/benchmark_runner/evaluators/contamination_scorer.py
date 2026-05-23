from __future__ import annotations

from benchmark_runner.models import TaskDefinition
from benchmark_runner.scoring import contains_phrase


def score_contamination(task: TaskDefinition, answer_text: str) -> int:
    hits = sum(1 for phrase in task.distractors + task.answer_rubric.forbidden_phrases if contains_phrase(answer_text, phrase))
    if hits >= 2:
        return -2
    if hits == 1:
        return -1
    return 0
