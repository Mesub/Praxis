"""The Claude client.

The single door through which PRAXIS talks to a model. Everything else in the AI plane calls *this*,
and never the SDK directly — which is what makes three claims enforceable in one place instead of
hopefully:

* **the model is chosen by the task** (``models.py``), so a grader cannot be downgraded at a
  call site
* **the call is attributed to a learner and a concept** (``cost.py``), so nothing is spent
  anonymously
* **the answer is never logged** (``logging.py``), so the tutor's ignorance survives contact
  with the observability stack

It is also the reason the oracle in M5.3 can be trusted: there is exactly one place a prompt can be
assembled, and it can be read in full in under a minute.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Any, Protocol

from praxis_ai.config import Settings
from praxis_ai.cost import Attribution, Cost, price_of
from praxis_ai.models import Task, model_for, tier_for

log = logging.getLogger("praxis.ai.claude")


class ModelUnavailableError(RuntimeError):
    """No key, or the model refused. PRAXIS says so plainly rather than inventing an answer."""


class _Messages(Protocol):
    async def create(self, **kwargs: Any) -> Any: ...


class _Client(Protocol):
    """Just enough of ``AsyncAnthropic`` to call it — and to replace it in a test.

    Depending on a protocol rather than the concrete SDK is what lets every test in this suite run
    on a machine that has never seen an API key.
    """

    messages: _Messages


@dataclass(frozen=True, slots=True)
class Completion:
    """What came back, and what it cost.

    The cost travels *with the response* rather than accumulating in this process, because the AI
    plane is stateless. The .NET Ledger (E16, M10) is what remembers.
    """

    text: str
    model: str
    task: Task
    cost: Cost
    stop_reason: str | None


class ClaudeClient:
    def __init__(self, settings: Settings, client: _Client | None = None) -> None:
        self._settings = settings
        self._client = client

        if client is None and settings.has_api_key:
            from anthropic import AsyncAnthropic

            self._client = AsyncAnthropic(
                api_key=settings.anthropic_api_key.get_secret_value(),
                max_retries=3,
            )

    @property
    def is_available(self) -> bool:
        return self._client is not None

    async def complete(
        self,
        prompt: str,
        *,
        task: Task,
        attribution: Attribution,
        system: str | None = None,
        max_tokens: int = 1024,
    ) -> Completion:
        """One call.

        ``task`` is not a hint — it *selects the model*. ``attribution`` is not metadata: without
        it there is no call, because a learner's money is not PRAXIS's to spend anonymously.
        """
        if self._client is None:
            raise ModelUnavailableError(
                "No ANTHROPIC_API_KEY is configured, so PRAXIS cannot call a model. It will not "
                "fabricate a response to keep a screen full."
            )

        model = model_for(task, self._settings)

        kwargs: dict[str, Any] = {
            "model": model,
            "max_tokens": max_tokens,
            "messages": [{"role": "user", "content": prompt}],
        }

        if system is not None:
            kwargs["system"] = system

        response = await self._client.messages.create(**kwargs)

        cost = price_of(
            model,
            input_tokens=response.usage.input_tokens,
            output_tokens=response.usage.output_tokens,
        )

        # The learner-minute. Note what is here and what is not: who, about what, on which model,
        # for how much — and **not one word of the prompt or the response**, either of which may
        # contain the answer.
        log.info(
            "model call",
            extra={
                "learner_id": attribution.learner_id,
                "concept_id": attribution.concept_id,
                "task": task.value,
                "tier": tier_for(task).value,
                **cost.as_log_fields(),
            },
        )

        if not cost.is_priced:
            log.warning(
                "Call billed at an unknown rate",
                extra={"model": model, "learner_id": attribution.learner_id},
            )

        return Completion(
            text=_text_of(response),
            model=model,
            task=task,
            cost=cost,
            stop_reason=getattr(response, "stop_reason", None),
        )


def _text_of(response: Any) -> str:
    """The text blocks, joined. Anything else in the response is not this layer's business."""
    return "".join(
        block.text for block in response.content if getattr(block, "type", None) == "text"
    )
