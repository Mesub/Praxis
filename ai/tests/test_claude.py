"""The one door to the model.

Every test here runs against a **fake transport**. That is not a compromise — it is the point. A
suite that needs a live key is a suite that does not run, and a test that does not run protects
nothing. The one test that *does* need a key skips itself, by name, and says so.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass, field
from decimal import Decimal
from typing import Any

import pytest

from praxis_ai.claude import ClaudeClient, ModelUnavailableError
from praxis_ai.config import Settings
from praxis_ai.cost import Attribution
from praxis_ai.models import Task

ALICE = Attribution(learner_id="alice", concept_id="bm25")


# ── A fake Anthropic ─────────────────────────────────────────────────────────


@dataclass
class _Block:
    text: str
    type: str = "text"


@dataclass
class _Usage:
    input_tokens: int = 1_000
    output_tokens: int = 500


@dataclass
class _Response:
    content: list[_Block]
    usage: _Usage = field(default_factory=_Usage)
    stop_reason: str = "end_turn"


class _FakeMessages:
    def __init__(self) -> None:
        self.calls: list[dict[str, Any]] = []

    async def create(self, **kwargs: Any) -> _Response:
        self.calls.append(kwargs)

        return _Response(content=[_Block("BM25 saturates term frequency.")])


class _FakeClient:
    def __init__(self) -> None:
        self.messages = _FakeMessages()


def _client(**overrides: Any) -> tuple[ClaudeClient, _FakeClient]:
    fake = _FakeClient()

    settings = Settings(anthropic_api_key="sk-ant-not-a-real-key", **overrides)

    return ClaudeClient(settings, client=fake), fake


# ── The task chooses the model ───────────────────────────────────────────────


async def test_a_grading_call_goes_to_the_heavy_model() -> None:
    client, fake = _client()

    await client.complete("mark this", task=Task.GRADE, attribution=ALICE)

    assert fake.messages.calls[0]["model"] == "claude-opus-4-8"


async def test_a_tutor_turn_goes_to_the_fast_model() -> None:
    client, fake = _client()

    await client.complete("ask a question", task=Task.TUTOR_TURN, attribution=ALICE)

    assert fake.messages.calls[0]["model"] == "claude-sonnet-5"


async def test_the_same_prompt_routes_differently_by_task_alone() -> None:
    """The model is a property of the *task* — not of the prompt, nor of who is calling."""
    client, fake = _client()

    await client.complete("identical text", task=Task.GRADE, attribution=ALICE)
    await client.complete("identical text", task=Task.TUTOR_TURN, attribution=ALICE)

    assert fake.messages.calls[0]["model"] != fake.messages.calls[1]["model"]


# ── Cost and attribution ─────────────────────────────────────────────────────


async def test_the_cost_comes_back_with_the_answer() -> None:
    """The AI plane is stateless, so it does not *accumulate* cost — it *reports* it, and the .NET
    Ledger (M10) is what remembers."""
    client, _ = _client()

    completion = await client.complete("mark this", task=Task.GRADE, attribution=ALICE)

    # 1000 input + 500 output on Opus: (1000*15 + 500*75) / 1e6
    assert completion.cost.usd == Decimal("0.0525")
    assert completion.cost.input_tokens == 1_000
    assert completion.cost.output_tokens == 500


async def test_a_call_cannot_be_made_without_saying_who_it_is_for() -> None:
    """``attribution`` is keyword-only and has no default. You cannot forget it; you can only
    refuse to supply it, and then it does not compile."""
    client, _ = _client()

    with pytest.raises(TypeError):
        await client.complete("mark this", task=Task.GRADE)   # type: ignore[call-arg]


async def test_the_learner_minute_is_logged(caplog: pytest.LogCaptureFixture) -> None:
    client, _ = _client()

    with caplog.at_level(logging.INFO, logger="praxis.ai.claude"):
        await client.complete("mark this", task=Task.GRADE, attribution=ALICE)

    record = next(r for r in caplog.records if r.message == "model call")

    assert record.learner_id == "alice"
    assert record.concept_id == "bm25"
    assert record.task == "grade"
    assert record.tier == "heavy"
    assert record.usd == pytest.approx(0.0525)


async def test_neither_the_prompt_nor_the_response_is_logged(
    caplog: pytest.LogCaptureFixture,
) -> None:
    """**The muzzle, at the one place a prompt exists.**

    Either side of this call may contain the answer — the grader is *given* the answer key, and the
    response may quote it back. So the learner-minute records who, about what, on which model, for
    how much, and **not one word of what was said**.
    """
    client, _ = _client()

    with caplog.at_level(logging.INFO):
        await client.complete(
            "The expected answer is: term frequency saturation.",
            task=Task.GRADE,
            attribution=ALICE,
        )

    logged = "\n".join(r.getMessage() + str(vars(r)) for r in caplog.records)

    assert "saturation" not in logged
    assert "expected answer" not in logged


# ── Refusal ──────────────────────────────────────────────────────────────────


async def test_without_a_key_it_refuses_rather_than_inventing_an_answer() -> None:
    client = ClaudeClient(Settings(anthropic_api_key=""))

    assert client.is_available is False

    with pytest.raises(ModelUnavailableError, match="will not fabricate"):
        await client.complete("anything", task=Task.TUTOR_TURN, attribution=ALICE)


# ── The live call ────────────────────────────────────────────────────────────


@pytest.mark.skipif(
    not os.environ.get("ANTHROPIC_API_KEY"),
    reason="no ANTHROPIC_API_KEY — the live call is not run, and this is NOT a pass",
)
async def test_a_real_call_to_a_real_model_returns_real_text_and_a_real_bill() -> None:
    """The only test here that spends money. It is skipped by name when there is no key, because a
    skip that pretends to be a pass is the exact dishonesty this whole project is against."""
    client = ClaudeClient(Settings())

    completion = await client.complete(
        "Reply with exactly the word: ok",
        task=Task.TUTOR_TURN,
        attribution=ALICE,
        max_tokens=16,
    )

    assert completion.text.strip()
    assert completion.cost.input_tokens > 0
    assert completion.cost.output_tokens > 0
    assert completion.cost.is_priced
    assert completion.model == "claude-sonnet-5"
