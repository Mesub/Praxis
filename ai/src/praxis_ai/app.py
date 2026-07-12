"""The AI plane.

**Stateless.** No database, no session, no memory of the last request. Everything it needs arrives
in the request. That is not an optimisation — it is what makes the rest of the architecture
legible: the .NET core owns the truth, the event log owns the history, and this process owns
*nothing*, so there is nowhere for a second version of a learner to accumulate.

What lands here over M5:

* the Claude client and model router (M5.2)
* **the ``check_claim`` oracle (M5.3)** — the architectural heart of the refusal. It holds the
  answer key and answers ``{correct, partial, wrong}``. The tutor calls it. The tutor never sees the
  answer, because the answer is never placed in the tutor's context.
* the rubric grader, whose most important output is **abstention** (M5.4)
* the item generator (M5.5) and the golden-set eval gate (M5.6)
"""

from __future__ import annotations

import logging
import time
import uuid
from contextlib import asynccontextmanager
from typing import Annotated, Any

from fastapi import Depends, FastAPI, Request

from praxis_ai.config import Settings, get_settings
from praxis_ai.cost import assert_priced
from praxis_ai.logging import configure_logging, request_id
from praxis_ai.models import Task, model_for
from praxis_ai.routes import assessment, oracle, tutor

log = logging.getLogger("praxis.ai")


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings = get_settings()

    configure_logging(level=settings.log_level, json_output=settings.log_json)

    # Note what is *not* logged: the key. Only whether there is one.
    log.info(
        "AI plane starting",
        extra={"environment": settings.environment, "api_key_configured": settings.has_api_key},
    )

    if not settings.has_api_key:
        # A warning, not a crash. The scaffold, the linter and the tests must all run on a machine
        # that has never seen a key — otherwise the first thing a new contributor meets is a wall.
        log.warning(
            "No ANTHROPIC_API_KEY is configured. Every route that needs a model will refuse, "
            "and say so plainly rather than pretending. Copy .env.example to .env to fix it."
        )

    # **A crash, and deliberately so.** Every model PRAXIS routes to must have a price before it
    # serves anybody. An unpriced model would be billed to a learner at a rate nobody knows and
    # recorded against them as if it were free — and the first anyone would hear of it is the
    # invoice. Failing here is a nuisance for us; failing later is a nuisance for them.
    assert_priced({model_for(task, settings) for task in Task})

    yield

    log.info("AI plane stopping")


app = FastAPI(
    title="PRAXIS AI plane",
    description="Stateless. It does not hold the answer.",
    version="0.1.0",
    lifespan=lifespan,
)

app.include_router(oracle.router)
app.include_router(assessment.router)
app.include_router(tutor.router)


@app.middleware("http")
async def tag_and_time(request: Request, call_next):
    """One id per request, so every line and every token of cost ties back to one learner-minute."""
    token = request_id.set(request.headers.get("x-request-id") or str(uuid.uuid4()))

    started = time.perf_counter()

    try:
        response = await call_next(request)
    finally:
        elapsed_ms = (time.perf_counter() - started) * 1000

        log.info(
            "%s %s",
            request.method,
            request.url.path,
            extra={"path": request.url.path, "elapsed_ms": round(elapsed_ms, 2)},
        )

    response.headers["x-request-id"] = request_id.get() or ""

    request_id.reset(token)

    return response


@app.get("/health")
async def health(settings: Annotated[Settings, Depends(get_settings)]) -> dict[str, Any]:
    """Liveness, and an honest account of what this process can actually do.

    ``model_configured`` is a boolean and will never be anything else. It says *whether* a key is
    present, never what it is — and it deliberately does not claim the key **works**. Proving that
    would mean spending a learner's money on a real call, and a health check that quietly bills
    someone is not a health check.
    """
    return {
        "status": "ok",
        "service": "praxis-ai",
        "environment": settings.environment,
        "model_configured": settings.has_api_key,
    }
