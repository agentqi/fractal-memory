from __future__ import annotations

from typing import Iterable

from benchmark_runner.models import RetrievalResult, ScoreBreakdown, TaskDefinition


def contains_phrase(text: str, phrase: str) -> bool:
    normalized_text = " ".join(text.lower().split())
    normalized_phrase = " ".join(phrase.lower().split())
    return normalized_phrase in normalized_text


def compute_branch_purity(task: TaskDefinition, result: RetrievalResult) -> float:
    if not result.retrieved_items:
        return 0.0
    relevant = sum(1 for item in result.retrieved_items if is_relevant_path(task, item.path))
    return round(relevant / len(result.retrieved_items), 4)


def is_relevant_path(task: TaskDefinition, path: str) -> bool:
    expected_paths = task.expected_paths or [f"projects/{task.project}"]
    lowered_path = path.lower()
    if any(expected.lower() in lowered_path for expected in expected_paths):
        return True
    if "handoffs" in lowered_path and task.project.replace("_", "-") in lowered_path:
        return True
    return False


def compute_precision(task: TaskDefinition, result: RetrievalResult) -> float:
    if not result.retrieved_items:
        return 0.0
    relevant = sum(1 for item in result.retrieved_items if is_relevant_path(task, item.path))
    return round(relevant / len(result.retrieved_items), 4)


def compute_recall(task: TaskDefinition, result: RetrievalResult) -> float:
    if not task.must_recover:
        return 1.0
    recovered = sum(1 for phrase in task.must_recover if contains_phrase(result.raw_output, phrase))
    return round(recovered / len(task.must_recover), 4)


def score_resume_accuracy(task: TaskDefinition, result: RetrievalResult) -> int:
    project_points = score_phrase_group(result.raw_output, [task.project.replace("-", " "), task.project], 2)
    objective_points = score_phrase_group(result.raw_output, task.must_recover[:1], 2)
    decision_points = score_phrase_group(result.raw_output, task.must_recover[1:2] + task.useful_to_recover[:1], 2)
    constraint_points = score_phrase_group(result.raw_output, task.useful_to_recover[1:2] + task.must_recover[2:3], 2)
    next_action_points = score_phrase_group(result.raw_output, task.must_recover[2:] + task.success_criteria[:1], 2)
    return project_points + objective_points + decision_points + constraint_points + next_action_points


def score_phrase_group(text: str, phrases: Iterable[str], max_points: int) -> int:
    filtered = [phrase for phrase in phrases if phrase]
    if not filtered:
        return max_points
    hits = sum(1 for phrase in filtered if contains_phrase(text, phrase))
    if hits == 0:
        return 0
    if hits >= len(filtered):
        return max_points
    return 1


def compute_penalty_irrelevant_context(task: TaskDefinition, result: RetrievalResult, precision: float) -> int:
    if result.estimated_tokens_loaded > 1800 or precision < 0.4:
        return -2
    if result.estimated_tokens_loaded > 900 or precision < 0.75:
        return -1
    return 0


def compute_penalty_contamination(task: TaskDefinition, result: RetrievalResult, branch_purity: float) -> int:
    distractor_hits = sum(1 for distractor in task.distractors if contains_phrase(result.raw_output, distractor))
    if branch_purity < 0.4 or distractor_hits >= 2:
        return -2
    if branch_purity < 0.8 or distractor_hits == 1:
        return -1
    return 0


def compute_penalty_wrong_claim(task: TaskDefinition, result: RetrievalResult) -> int:
    wrong_signals = [d for d in task.distractors if contains_phrase(result.raw_output, d)]
    must_hits = sum(1 for phrase in task.must_recover if contains_phrase(result.raw_output, phrase))
    if wrong_signals and must_hits == 0:
        return -2
    if wrong_signals:
        return -1
    return 0


def score_task(task: TaskDefinition, result: RetrievalResult) -> ScoreBreakdown:
    raw = score_resume_accuracy(task, result)
    precision = compute_precision(task, result)
    recall = compute_recall(task, result)
    branch_purity = compute_branch_purity(task, result)
    penalty_irrelevant = compute_penalty_irrelevant_context(task, result, precision)
    penalty_contamination = compute_penalty_contamination(task, result, branch_purity)
    penalty_wrong_claim = compute_penalty_wrong_claim(task, result)
    adjusted = raw + penalty_irrelevant + penalty_contamination + penalty_wrong_claim
    return ScoreBreakdown(
        resume_accuracy_raw=raw,
        penalty_irrelevant_context=penalty_irrelevant,
        penalty_contamination=penalty_contamination,
        penalty_wrong_claim=penalty_wrong_claim,
        resume_accuracy_adjusted=adjusted,
        retrieval_precision=precision,
        retrieval_recall=recall,
        branch_purity=branch_purity,
        notes="",
    )
