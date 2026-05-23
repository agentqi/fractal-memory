from __future__ import annotations

from benchmark_runner.llm.answer_models import LlmRequest
from benchmark_runner.models import RetrievalResult, TaskDefinition


DEFAULT_SYSTEM_PROMPT = (
    "You are an assistant helping with a long-running project workflow.\n"
    "Use only the provided retrieved memory and the user's current prompt.\n"
    "Do not claim continuity that is not supported by the retrieved memory.\n"
    "If relevant information is missing, say so.\n"
    "Prefer precise, branch-correct answers over broad speculation."
)

DEFAULT_ANSWER_INSTRUCTIONS = (
    "Return your answer in exactly these sections:\n"
    "1. Project / Branch\n"
    "2. Current Objective\n"
    "3. Key Prior Decision\n"
    "4. Active Constraint\n"
    "5. Next Best Action\n"
    "6. Missing Information\n"
    "Keep the answer under 180 words.\n"
    "Do not add implementation plans unless the prompt explicitly asks for them.\n"
    "Prefer direct restatement of retrieved facts over paraphrased expansion.\n"
    "If a section is not supported by the retrieved memory, write: Not supported by retrieved memory."
)

CATEGORY_GUIDANCE = {
    "resume-after-reset": (
        "For this task, keep the answer tightly focused on the resumed project state.\n"
        "Use the sections to name the current objective, one prior decision, one active constraint, and one next action."
    ),
    "branch-selective-retrieval": (
        "For this task, stay narrow.\n"
        "The Key Prior Decision section should carry the main answer.\n"
        "Do not add extra planning beyond one short next action if supported."
    ),
    "handoff-resume": (
        "For this task, emphasize the latest handoff facts.\n"
        "Current Objective should reflect the handoff goal.\n"
        "Next Best Action should be the next three short actions only if supported."
    ),
    "cross-project-separation": (
        "For this task, answer only for the requested project or branch.\n"
        "Do not mention unrelated projects unless the retrieved memory explicitly says they are relevant."
    ),
}


def build_prompt(task: TaskDefinition, retrieval: RetrievalResult, model_name: str, temperature: float, max_tokens: int) -> LlmRequest:
    answer_instructions = DEFAULT_ANSWER_INSTRUCTIONS
    category_guidance = CATEGORY_GUIDANCE.get(task.category)
    if category_guidance:
        answer_instructions = f"{answer_instructions}\n\nCategory guidance:\n{category_guidance}"
    if task.short_answer_target is not None:
        answer_instructions = f"{answer_instructions}\n\nReference short-form target:\n{_render_short_answer_target(task)}"
    return LlmRequest(
        system_prompt=DEFAULT_SYSTEM_PROMPT,
        user_prompt=task.prompt,
        retrieved_context=retrieval.raw_output,
        answer_instructions=answer_instructions,
        model_name=model_name,
        temperature=temperature,
        max_tokens=max_tokens,
        metadata={
            "task_id": task.id,
            "project": task.project,
            "approach_name": retrieval.approach_name,
        },
    )


def dry_run_prompt(request: LlmRequest) -> str:
    return (
        "[SYSTEM]\n"
        f"{request.system_prompt}\n\n"
        "[RETRIEVED MEMORY]\n"
        f"{request.retrieved_context}\n\n"
        "[USER TASK]\n"
        f"{request.user_prompt}\n\n"
        "[ANSWER INSTRUCTIONS]\n"
        f"{request.answer_instructions}"
    ).strip()


def _render_short_answer_target(task: TaskDefinition) -> str:
    target = task.short_answer_target
    if target is None:
        return ""
    ordered_fields = [
        ("Project / Branch", target.project_branch),
        ("Current Objective", target.current_objective),
        ("Key Prior Decision", target.key_prior_decision),
        ("Active Constraint", target.active_constraint),
        ("Next Best Action", target.next_best_action),
        ("Missing Information", target.missing_information),
    ]
    lines = []
    for label, value in ordered_fields:
        if value:
            lines.append(f"- {label}: {value}")
    return "\n".join(lines)
