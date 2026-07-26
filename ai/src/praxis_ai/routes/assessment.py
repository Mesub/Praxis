"""``/assessment`` — grading a response, and writing an item.

The other two things the .NET core asks this service to do. Note what crosses the wire and what does
not:

* **In:** the learner's response and *which concept* — never a rubric, which is answer-adjacent and
  lives here, resolved from the concept (M11.7b). And ``learner_id`` + ``concept_id`` on *every*
  call — because the AI plane is stateless and has no idea who is talking to it, and because nothing
  is spent anonymously (M5.2).
* **Out:** a verdict, or an item. Never an answer, never a rubric band, never the model's free text.
"""

from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from praxis_ai.claude import ClaudeClient, ModelUnavailableError
from praxis_ai.config import Settings, get_settings
from praxis_ai.cost import Attribution
from praxis_ai.grading import GradeOutcome, Grader
from praxis_ai.items import EvidenceType, ItemFormat, ItemGenerator, ItemRejectedError
from praxis_ai.oracle import AnswerKeyStore
from praxis_ai.routes.oracle import get_store
from praxis_ai.rubric_store import (
    RubricStore,
    UnknownRubricError,
    default_rubric_store,
    require,
)

router = APIRouter(prefix="/assessment", tags=["assessment"])

# The rubric library lives here, on the AI plane — answer-adjacent content never crosses back to
# the core (C-5). A process-wide singleton, seeded at import, mirroring the oracle's answer-key
# store. The grade-by-concept endpoint that consumes it is the next slice (M11.7b).
_rubric_store = default_rubric_store()


def get_rubric_store() -> RubricStore:
    return _rubric_store


# ── Grading ──────────────────────────────────────────────────────────────────
#
# There is one grade door and the caller does not bring the standard to it. The old ``/grade``,
# which took a full rubric over the wire, has been retired (M11.7b): a rubric is answer-adjacent, so
# it lives on this side and the core names the concept instead. One door, and no rubric crosses it.


class GradeResponse(BaseModel):
    """**`pending` is a real outcome and the core must handle it as one** (R-1).

    It is not a slow `passed`. The .NET side writes it as `PENDING` evidence, which cannot move
    mastery, and a human decides. There is no field here that lets the caller resolve it themselves.
    """

    outcome: GradeOutcome
    score: float | None = None
    misconceptions: list[str] = Field(default_factory=list)

    reason: str
    judges_consulted: int
    escalated_to_adjudicator: bool
    escalated_to_human: bool

    usd: float | None = None


# ── Grading a concept, with the rubric resolved here ──────────────────────────
#
# The teach-back path (M11.7b). The core sends only the learner's words and *which concept* — never
# a rubric, because the rubric is answer-adjacent and lives on this side of the wire (M11.7a). The
# grader looks it up and grades against it; the rubric never crosses back. If no rubric is authored
# for the concept, PRAXIS declines (422) rather than inventing a standard on the spot.


class GradeByConceptRequest(BaseModel):
    response: str = Field(min_length=1, max_length=40_000)

    learner_id: str = Field(min_length=1)
    concept_id: str = Field(min_length=1)


@router.post("/grade-by-concept", response_model=GradeResponse)
async def grade_by_concept(
    request: GradeByConceptRequest,
    settings: Annotated[Settings, Depends(get_settings)],
    rubrics: Annotated[RubricStore, Depends(get_rubric_store)],
) -> GradeResponse:
    try:
        rubric = require(rubrics, request.concept_id)
    except UnknownRubricError as unknown:
        raise HTTPException(status_code=422, detail=str(unknown)) from unknown

    grader = Grader(ClaudeClient(settings))

    try:
        result = await grader.grade(
            request.response,
            rubric,
            Attribution(learner_id=request.learner_id, concept_id=request.concept_id),
        )
    except ModelUnavailableError as unavailable:
        raise HTTPException(status_code=503, detail=str(unavailable)) from unavailable

    return GradeResponse(
        outcome=result.outcome,
        score=result.score,
        misconceptions=list(result.misconceptions),
        reason=result.reason,
        judges_consulted=result.judges_consulted,
        escalated_to_adjudicator=result.escalated_to_adjudicator,
        escalated_to_human=result.escalated_to_human,
        usd=None if result.usd is None else float(result.usd),
    )


# ── Item generation ──────────────────────────────────────────────────────────


class GenerateItemRequest(BaseModel):
    concept_id: str = Field(min_length=1)
    concept_summary: str = Field(min_length=1)
    evidence_type: EvidenceType
    difficulty: float = Field(ge=0, le=1)

    learner_id: str = Field(min_length=1)

    #: How items for this concept are marked. Declared by the pack, carried by the core.
    item_format: ItemFormat = ItemFormat.PROSE

    #: Fingerprints of what this learner has already been shown. **The core remembers; this service
    #: does not.** Novelty is checked against these, deterministically (M5.5).
    already_seen: list[str] = Field(default_factory=list)

    #: The concept's **declared** wrong beliefs, keyed by a short stable slug. The generator is told
    #: what they are and may name one in the response's `targets_misconception` — but only a key
    #: from this map ever survives the airlock in `items.py` (M14.2).
    known_misconceptions: dict[str, str] = Field(default_factory=dict)


class ItemResponse(BaseModel):
    """**Everything the core, the tutor and the learner ever see of an item.**

    There is no `answer` field, and there must never be one. The answer exists — it went to the
    oracle's store the moment the item was made — and the only thing that ever comes back out of it
    is one of three words (M5.3).
    """

    id: str
    concept_id: str
    evidence_type: EvidenceType
    stem: str
    difficulty: float

    #: The text under study, for a reading item. The learner reads it; it is not secret.
    passage: str | None = None

    #: The choices as shown, in order. Which one is correct is not here and never will be.
    options: list[str] | None = None

    fingerprint: str

    #: A key from `GenerateItemRequest.known_misconceptions`, or null. Never free text — the airlock
    #: in `items.py` guarantees that.
    targets_misconception: str | None = None


@router.post("/items", response_model=ItemResponse)
async def generate_item(
    request: GenerateItemRequest,
    settings: Annotated[Settings, Depends(get_settings)],
    store: Annotated[AnswerKeyStore, Depends(get_store)],
) -> ItemResponse:
    generator = ItemGenerator(ClaudeClient(settings), store)   # type: ignore[arg-type]

    try:
        item = await generator.generate(
            concept_id=request.concept_id,
            concept_summary=request.concept_summary,
            evidence_type=request.evidence_type,
            difficulty=request.difficulty,
            attribution=Attribution(
                learner_id=request.learner_id, concept_id=request.concept_id
            ),
            already_seen=frozenset(request.already_seen),
            known_misconceptions=request.known_misconceptions,
            item_format=request.item_format,
        )
    except ItemRejectedError as rejected:
        raise HTTPException(status_code=422, detail=str(rejected)) from rejected
    except ModelUnavailableError as unavailable:
        raise HTTPException(status_code=503, detail=str(unavailable)) from unavailable

    return ItemResponse(
        id=item.id,
        concept_id=item.concept_id,
        evidence_type=item.evidence_type,
        stem=item.stem,
        difficulty=item.difficulty,
        passage=item.passage,
        options=None if item.options is None else list(item.options),
        fingerprint=item.fingerprint,
        targets_misconception=item.targets_misconception,
    )
