from __future__ import annotations

from benchmark_runner.models import TaskDefinition
from benchmark_runner.scoring import contains_phrase, score_phrase_group


def score_final_answer_rubric(task: TaskDefinition, answer_text: str) -> int:
    project_points = score_phrase_group(answer_text, [task.project.replace("-", " "), task.project], 2)
    objective_points = score_phrase_group(answer_text, task.must_recover[:1], 2)
    decision_points = score_phrase_group(answer_text, task.must_recover[1:2] + task.useful_to_recover[:1], 2)
    constraint_points = score_phrase_group(answer_text, task.useful_to_recover[1:2] + task.must_recover[2:3], 2)
    next_action_points = score_phrase_group(answer_text, task.success_criteria[:1] + task.must_recover[2:], 2)
    return project_points + objective_points + decision_points + constraint_points + next_action_points


def score_task_completion(task: TaskDefinition, answer_text: str) -> int:
    usefulness = 2 if any(contains_phrase(answer_text, phrase) for phrase in task.must_recover) else 0
    actionability = 2 if any(contains_phrase(answer_text, phrase) for phrase in task.success_criteria + ["next", "action", "review", "finish"]) else 0
    clarity = 2 if len(answer_text.splitlines()) >= 2 else 1 if answer_text.strip() else 0
    return usefulness + actionability + clarity
