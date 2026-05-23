from __future__ import annotations

from benchmark_runner.evaluators.contamination_scorer import score_contamination
from benchmark_runner.evaluators.continuity_claim_scorer import score_hallucinated_continuity
from benchmark_runner.evaluators.rubric import score_final_answer_rubric, score_task_completion
from benchmark_runner.llm.answer_models import LlmResponse
from benchmark_runner.models import AnswerScore, RetrievalResult, TaskDefinition
from benchmark_runner.scoring import compute_recall, contains_phrase


class RuleBasedAnswerScorer:
    REQUIRED_SECTIONS = [
        "1. Project / Branch",
        "2. Current Objective",
        "3. Key Prior Decision",
        "4. Active Constraint",
        "5. Next Best Action",
        "6. Missing Information",
    ]

    def score(self, task: TaskDefinition, answer: LlmResponse, retrieval: RetrievalResult) -> AnswerScore:
        raw = score_final_answer_rubric(task, answer.content)
        contamination_penalty = score_contamination(task, answer.content)
        hallucinated_continuity_penalty = score_hallucinated_continuity(task, answer.content, retrieval.raw_output)
        unsupported_claim_penalty = -1 if self._has_unsupported_specific_claim(task, answer.content, retrieval.raw_output) else 0
        verbosity_penalty = -1 if len(answer.content.split()) > 180 else 0
        format_adherence_score = self._score_format_adherence(answer.content)
        adjusted = raw + contamination_penalty + hallucinated_continuity_penalty + unsupported_claim_penalty + verbosity_penalty
        answer_recall = compute_recall(task, RetrievalResult(approach_name=retrieval.approach_name, raw_output=answer.content))
        must_hits = sum(1 for phrase in task.must_recover if contains_phrase(answer.content, phrase))
        answer_precision = round(must_hits / max(1, len(task.must_recover)), 4)
        task_completion_score = score_task_completion(task, answer.content)
        continuity_confidence_score = 2 if "missing" in answer.content.lower() or "unsure" in answer.content.lower() else 1
        return AnswerScore(
            final_answer_score_raw=raw,
            contamination_penalty=contamination_penalty,
            hallucinated_continuity_penalty=hallucinated_continuity_penalty,
            unsupported_claim_penalty=unsupported_claim_penalty,
            verbosity_penalty=verbosity_penalty,
            format_adherence_score=format_adherence_score,
            final_answer_score_adjusted=adjusted,
            answer_precision=answer_precision,
            answer_recall=answer_recall,
            task_completion_score=task_completion_score,
            continuity_confidence_score=continuity_confidence_score,
            notes="",
        )

    @staticmethod
    def _has_unsupported_specific_claim(task: TaskDefinition, answer_text: str, retrieved_context: str) -> bool:
        for phrase in task.answer_rubric.expected_phrases:
            if contains_phrase(answer_text, phrase) and not contains_phrase(retrieved_context, phrase):
                return True
        return False

    @classmethod
    def _score_format_adherence(cls, answer_text: str) -> int:
        found = sum(1 for section in cls.REQUIRED_SECTIONS if section in answer_text)
        if found == len(cls.REQUIRED_SECTIONS):
            return 2
        if found >= 4:
            return 1
        return 0
