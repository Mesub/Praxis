"""``POST /oracle/check-claim`` — the tutor's only way to find out if a learner is right.

Note the shape of this endpoint, and the shape it does **not** have. There is no
``GET /items/{id}/answer``. There is no ``?include_answer=true``. There is no admin route, no debug
route, and no "just for the grader" route. The answer key has exactly one consumer in this entire
system, and it is the function directly below.

If a route is ever added that returns an answer to a caller, every claim PRAXIS makes about its
tutor becomes marketing.
"""

from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel, Field

from praxis_ai.claude import ClaudeClient, ModelUnavailableError
from praxis_ai.config import Settings, get_settings
from praxis_ai.cost import Attribution
from praxis_ai.key_store import PostgresAnswerKeyStore
from praxis_ai.oracle import (
    AnswerKeyStore,
    ClaimOutcome,
    InMemoryAnswerKeyStore,
    Oracle,
    OracleUnclearError,
    UnknownItemError,
)

router = APIRouter(prefix="/oracle", tags=["oracle"])

#: Populated by the item generator (M5.5). Durable when ``oracle_store_url`` is configured
#: (M14.1); in-memory otherwise, which is what tests and a cold dev checkout get. The *shape* of
#: the store is what matters: a ``get``, and nothing that lists or searches or hands anything out.
_store: AnswerKeyStore | None = None


def get_store(settings: Annotated[Settings, Depends(get_settings)]) -> AnswerKeyStore:
    global _store  # a process-wide singleton, built once from settings

    if _store is None:
        if settings.has_oracle_store:
            durable = PostgresAnswerKeyStore(settings.oracle_store_url.get_secret_value())
            durable.ensure_schema()
            _store = durable
        else:
            _store = InMemoryAnswerKeyStore()

    return _store


class CheckClaimRequest(BaseModel):
    item_id: str = Field(min_length=1)

    #: What the learner actually said. It goes to the oracle and nowhere else.
    claim: str = Field(min_length=1, max_length=8_000)

    learner_id: str = Field(min_length=1)
    concept_id: str = Field(min_length=1)


class CheckClaimResponse(BaseModel):
    """**Everything the tutor is ever told.** Three words, and possibly a key.

    A ``correct_answer`` field must never be added here, however convenient it would be for the
    "reveal the answer at the end of the session" feature somebody will eventually ask for. If that
    feature is ever built, it must be built somewhere the tutor's context cannot reach.
    """

    outcome: ClaimOutcome
    misconception: str | None = None

    usd: float | None = None


@router.post("/check-claim", response_model=CheckClaimResponse)
async def check_claim(
    request: CheckClaimRequest,
    settings: Annotated[Settings, Depends(get_settings)],
    store: Annotated[AnswerKeyStore, Depends(get_store)],
) -> CheckClaimResponse:
    oracle = Oracle(ClaudeClient(settings), store)

    try:
        verdict = await oracle.check(
            request.item_id,
            request.claim,
            Attribution(learner_id=request.learner_id, concept_id=request.concept_id),
        )
    except UnknownItemError as unknown:
        raise HTTPException(status_code=404, detail=str(unknown)) from unknown
    except OracleUnclearError as unclear:
        # 422, not 200-with-a-wrong. The tutor will tell the learner it could not check that and ask
        # them to say it another way. It will **not** tell them they were wrong, because they may
        # well have been right, and the failure was ours.
        raise HTTPException(status_code=422, detail=str(unclear)) from unclear
    except ModelUnavailableError as unavailable:
        raise HTTPException(status_code=503, detail=str(unavailable)) from unavailable

    return CheckClaimResponse(
        outcome=verdict.outcome,
        misconception=verdict.misconception,
        usd=None if verdict.cost.usd is None else float(verdict.cost.usd),
    )
