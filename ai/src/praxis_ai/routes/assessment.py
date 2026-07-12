"""``/assessment`` — grading a response, and writing an item.

The other two things the .NET core asks this service to do. Note what crosses the wire and what does
not:

* **In:** the learner's response, the rubric, the concept. And ``learner_id`` + ``concept_id``, on
  *every* call — because the AI plane is stateless and has no idea who is talking to it, and because
  nothing is spent anonymously (M5.2).
* **Out:** a verdict, or an item. Never an answer, never a rubric band the caller did not already
  have, never the model's free text.
"""

from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from praxis_ai.claude import ClaudeClient, ModelUnavailableError
from praxis_ai.config import Settings, get_settings
from praxis_ai.cost import Attribution
from praxis_ai.grading import GradeOutcome, Grader
from praxis_ai.items import EvidenceType, ItemGenerator, ItemRejectedError
from praxis_ai.oracle import AnswerKeyStore
from praxis_ai.routes.oracle import get_store
from praxis_ai.rubric import Dimension, HardFail, Rubric

router = APIRouter(prefix="/assessment", tags=["assessment"])


# ── Grading ──────────────────────────────────────────────────────────────────


class DimensionSpec(BaseModel):
    id: str = Field(min_length=1)
    weight: float = Field(gt=0, le=1)
    probe: str
    bands: dict[int, str]
    auto_flag_at_or_below: int | None = None
    auto_flag_misconception: str | None = None


class HardFailSpec(BaseModel):
    pattern: str
    misconception: str
    cap: float = Field(ge=0, le=1)


class RubricSpec(BaseModel):
    """The rubric travels *to* the grader on every call, because the AI plane remembers nothing.

    It is answer-adjacent (a band description is, in effect, the answer), which is exactly why it
    goes to the **grader** and never to the **tutor**, and why the tutor's endpoint takes no rubric.
    """

    id: str
    concept_id: str
    audience: str
    pass_score: float = Field(default=0.75, ge=0, le=1)
    min_length: int = 40
    dimensions: list[DimensionSpec] = Field(min_length=1)
    hard_fails: list[HardFailSpec] = Field(default_factory=list)

    def to_domain(self) -> Rubric:
        return Rubric(
            id=self.id,
            concept_id=self.concept_id,
            audience=self.audience,
            pass_score=self.pass_score,
            min_length=self.min_length,
            dimensions=tuple(
                Dimension(
                    id=d.id,
                    weight=d.weight,
                    probe=d.probe,
                    bands=d.bands,
                    auto_flag=(
                        (d.auto_flag_at_or_below, d.auto_flag_misconception)
                        if d.auto_flag_at_or_below is not None
                        and d.auto_flag_misconception is not None
                        else None
                    ),
                )
                for d in self.dimensions
            ),
            hard_fails=tuple(
                HardFail(pattern=h.pattern, misconception=h.misconception, cap=h.cap)
                for h in self.hard_fails
            ),
        )


class GradeRequest(BaseModel):
    response: str = Field(min_length=1, max_length=40_000)
    rubric: RubricSpec

    learner_id: str = Field(min_length=1)
    concept_id: str = Field(min_length=1)


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


@router.post("/grade", response_model=GradeResponse)
async def grade(
    request: GradeRequest,
    settings: Annotated[Settings, Depends(get_settings)],
) -> GradeResponse:
    grader = Grader(ClaudeClient(settings))

    try:
        result = await grader.grade(
            request.response,
            request.rubric.to_domain(),
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

    #: Fingerprints of what this learner has already been shown. **The core remembers; this service
    #: does not.** Novelty is checked against these, deterministically (M5.5).
    already_seen: list[str] = Field(default_factory=list)


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
    fingerprint: str


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
        fingerprint=item.fingerprint,
    )
