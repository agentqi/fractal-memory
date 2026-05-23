from __future__ import annotations

import re

from benchmark_runner.models import TaskDefinition
from benchmark_runner.scoring import contains_phrase


def score_hallucinated_continuity(task: TaskDefinition, answer_text: str, retrieved_context: str) -> int:
    penalties = 0
    lowered_context = retrieved_context.lower()
    for pattern in task.answer_rubric.unsupported_claim_patterns:
        if re.search(pattern, answer_text, flags=re.IGNORECASE) and not re.search(pattern, lowered_context, flags=re.IGNORECASE):
            penalties -= 1
    if penalties <= -2:
        return -2
    if penalties == -1:
        return -1
    continuity_claims = ["already finalized", "previously approved", "we already decided"]
    if any(contains_phrase(answer_text, claim) for claim in continuity_claims) and not any(contains_phrase(retrieved_context, claim) for claim in continuity_claims):
        return -1
    return 0
