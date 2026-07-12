"""``POST /tutor/turn`` — one turn of Socratic dialogue.

**Look at what the request cannot carry.** There is no ``answer`` field, no ``rubric``, no
``answer_key``. The .NET core could not send an answer to the tutor even if it had one — and it
does not have one, because the answer lives in the oracle's store on *this* side of the wire, and
comes out only as three words (M5.3).

So the guarantee survives the network hop, which is where most guarantees quietly die.
"""

from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from praxis_ai.claude import ClaudeClient, ModelUnavailableError
from praxis_ai.config import Settings, get_settings
from praxis_ai.cost import Attribution
from praxis_ai.tutor import HintRung, Turn, Tutor, build_context

router = APIRouter(prefix="/tutor", tags=["tutor"])


class TurnSpec(BaseModel):
    """One utterance. The learner's words are **data** — they land here and nowhere else."""

    who: str = Field(pattern="^(learner|tutor)$")
    said: str = Field(min_length=1, max_length=20_000)


class TutorTurnRequest(BaseModel):
    """**Everything the tutor is ever given.**

    No ``answer``. No ``rubric`` (its band descriptions *are* the answer — M5.4). No ``expected``.
    A field that does not exist on the wire cannot be populated by a caller having a bad day.
    """

    concept_summary: str = Field(min_length=1)

    #: The question the learner is looking at. They can see it; it is not a secret.
    item_stem: str = Field(min_length=1)

    transcript: list[TurnSpec] = Field(default_factory=list)

    #: The deepest rung the ladder has authorised (M6.2). There is no rung below `partial_scaffold`.
    rung: HintRung

    budget_remaining: int = Field(ge=0)

    #: Misconception **keys** only, from a closed declared set. Never free text.
    known_misconceptions: list[str] = Field(default_factory=list)

    learner_id: str = Field(min_length=1)
    concept_id: str = Field(min_length=1)


class TutorTurnResponse(BaseModel):
    text: str
    usd: float | None = None


@router.post("/turn", response_model=TutorTurnResponse)
async def turn(
    request: TutorTurnRequest,
    settings: Annotated[Settings, Depends(get_settings)],
) -> TutorTurnResponse:
    tutor = Tutor(ClaudeClient(settings))

    context = build_context(
        concept_summary=request.concept_summary,
        item_stem=request.item_stem,
        transcript=tuple(Turn(t.who, t.said) for t in request.transcript),
        rung=request.rung,
        budget_remaining=request.budget_remaining,
        known_misconceptions=tuple(request.known_misconceptions),
    )

    try:
        reply = await tutor.turn(
            context,
            Attribution(learner_id=request.learner_id, concept_id=request.concept_id),
        )
    except ModelUnavailableError as unavailable:
        raise HTTPException(status_code=503, detail=str(unavailable)) from unavailable

    return TutorTurnResponse(
        text=reply.text,
        usd=None if reply.cost.usd is None else float(reply.cost.usd),
    )
