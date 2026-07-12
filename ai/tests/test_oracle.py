"""**The tutor cannot give you the answer, and here is why that is a fact rather than a promise.**

The interesting tests in this file are not the happy paths. They are the ones where the *model
itself* is the adversary: it has been given the answer (it must be, to mark the claim), and these
tests make it try, in every way it can, to get that answer back out through the oracle's reply.

It cannot. Not because it was asked nicely in a system prompt, but because the only door out is two
enumerated fields, and neither of them is big enough for a sentence.
"""

from __future__ import annotations

import logging
from dataclasses import fields
from typing import Any

import pytest

from praxis_ai.claude import ClaudeClient
from praxis_ai.config import Settings
from praxis_ai.cost import Attribution
from praxis_ai.oracle import (
    AnswerKey,
    ClaimOutcome,
    InMemoryAnswerKeyStore,
    Oracle,
    OracleUnclearError,
    UnknownItemError,
    Verdict,
)

ALICE = Attribution(learner_id="alice", concept_id="bm25")

#: The dangerous string. If it appears anywhere outside the oracle's own prompt, PRAXIS has failed.
THE_ANSWER = "BM25 saturates term frequency, so a tenth occurrence adds far less than the second."

KEY = AnswerKey(
    item_id="item-1",
    concept_id="bm25",
    question="Why does BM25 not reward a term appearing 100 times ten times as much as 10 times?",
    answer=THE_ANSWER,
    misconceptions={
        "thinks-tf-is-linear": "Believes term frequency contributes linearly without saturation.",
        "confuses-tf-with-idf": "Attributes the saturation to inverse document frequency.",
    },
)


class _Model:
    """A model that says whatever the test tells it to say."""

    def __init__(self, reply: str) -> None:
        self.reply = reply
        self.prompts: list[dict[str, Any]] = []

        outer = self

        class _Messages:
            async def create(self, **kwargs: Any) -> Any:
                outer.prompts.append(kwargs)
                return _Response(outer.reply)

        self.messages = _Messages()


class _Response:
    def __init__(self, text: str) -> None:
        self.content = [type("Block", (), {"type": "text", "text": text})()]
        self.usage = type("Usage", (), {"input_tokens": 400, "output_tokens": 20})()
        self.stop_reason = "end_turn"


def _oracle(reply: str) -> tuple[Oracle, _Model]:
    model = _Model(reply)

    client = ClaudeClient(Settings(anthropic_api_key="sk-ant-not-real"), client=model)

    return Oracle(client, InMemoryAnswerKeyStore({KEY.item_id: KEY})), model


# ── It marks ─────────────────────────────────────────────────────────────────


async def test_a_correct_claim_is_correct() -> None:
    oracle, _ = _oracle('{"outcome": "correct", "misconception": null}')

    verdict = await oracle.check("item-1", "Because TF saturates.", ALICE)

    assert verdict.outcome is ClaimOutcome.CORRECT
    assert verdict.misconception is None


async def test_a_wrong_claim_names_the_misconception_it_matched() -> None:
    oracle, _ = _oracle('{"outcome": "wrong", "misconception": "thinks-tf-is-linear"}')

    verdict = await oracle.check("item-1", "Ten times as many, ten times the score.", ALICE)

    assert verdict.outcome is ClaimOutcome.WRONG
    assert verdict.misconception == "thinks-tf-is-linear"


async def test_the_oracle_sees_the_answer_because_it_must() -> None:
    """The point. Somebody has to hold the key — the design is about *who*, not *whether*."""
    oracle, model = _oracle('{"outcome": "partial", "misconception": null}')

    await oracle.check("item-1", "something", ALICE)

    assert THE_ANSWER in model.prompts[0]["messages"][0]["content"]


# ── The model tries to leak, and cannot ──────────────────────────────────────


async def test_the_answer_cannot_escape_through_the_misconception_field() -> None:
    """The obvious attack: the field is a string, so put the answer in it.

    The field only accepts keys the item's author wrote down **before this learner existed**. An
    answer is not one of them.
    """
    oracle, _ = _oracle(f'{{"outcome": "wrong", "misconception": "{THE_ANSWER}"}}')

    verdict = await oracle.check("item-1", "no idea", ALICE)

    assert verdict.misconception is None
    assert THE_ANSWER not in str(verdict)


async def test_the_answer_cannot_escape_through_extra_fields() -> None:
    """The model invents a helpful ``explanation`` key. Nothing reads it, so nothing carries it."""
    oracle, _ = _oracle(
        f'{{"outcome": "wrong", "misconception": null, "explanation": "{THE_ANSWER}", '
        f'"correct_answer": "{THE_ANSWER}", "hint": "{THE_ANSWER}"}}'
    )

    verdict = await oracle.check("item-1", "no idea", ALICE)

    assert THE_ANSWER not in str(verdict)
    assert THE_ANSWER not in repr(verdict)


async def test_the_answer_cannot_escape_as_prose_around_the_json() -> None:
    oracle, _ = _oracle(
        f'The learner is wrong. The correct answer is: {THE_ANSWER}\n'
        f'{{"outcome": "wrong", "misconception": null}}'
    )

    verdict = await oracle.check("item-1", "no idea", ALICE)

    assert verdict.outcome is ClaimOutcome.WRONG
    assert THE_ANSWER not in str(verdict)


async def test_a_model_that_ignores_its_instructions_entirely_leaks_nothing() -> None:
    """It replies in prose, helpfully, with the whole answer. It gets an error, not a channel."""
    oracle, _ = _oracle(f"The learner is close. The answer is: {THE_ANSWER}")

    with pytest.raises(OracleUnclearError):
        await oracle.check("item-1", "no idea", ALICE)


# ── The type itself is the guarantee ─────────────────────────────────────────


def test_there_is_no_field_on_a_verdict_that_could_hold_an_answer() -> None:
    """**The guarantee you can check by reading the type, rather than by trusting a prompt.**

    No explanation. No "you were close". No ``correct_answer``, not even an optional one, not even
    a nullable one that is "only populated once the budget is exhausted" — because the moment such
    a field exists, something will populate it, and the guarantee stops being a fact about the type
    and becomes a code path that someone can be talked into taking.
    """
    names = {f.name for f in fields(Verdict)}

    assert names == {"outcome", "misconception", "cost"}

    forbidden = {"answer", "correct_answer", "explanation", "hint", "feedback", "rationale", "text"}

    assert not (names & forbidden), f"A verdict can now carry {names & forbidden}."


def test_the_outcome_vocabulary_is_three_words_long() -> None:
    """A richer vocabulary is a wider channel. "You are missing two of the four required elements"
    is a number a patient learner can binary-search against."""
    assert {o.value for o in ClaimOutcome} == {"correct", "partial", "wrong"}


# ── Refusals ─────────────────────────────────────────────────────────────────


async def test_an_unknown_item_is_refused_rather_than_guessed_at() -> None:
    oracle, _ = _oracle('{"outcome": "correct", "misconception": null}')

    with pytest.raises(UnknownItemError, match="will not improvise"):
        await oracle.check("item-nobody-wrote", "anything", ALICE)


async def test_an_unparseable_reply_is_never_treated_as_wrong() -> None:
    """**A learner is never marked wrong because our parser failed.**

    Defaulting to WRONG would be the convenient thing, and it would write a false belief into a
    model of someone's mind on the strength of our own bug — then decay it, then build on it.
    """
    oracle, _ = _oracle("{ this is not json")

    with pytest.raises(OracleUnclearError):
        await oracle.check("item-1", "a genuinely correct answer", ALICE)


async def test_an_outcome_outside_the_vocabulary_is_refused() -> None:
    oracle, _ = _oracle('{"outcome": "sort-of-right-ish", "misconception": null}')

    with pytest.raises(OracleUnclearError, match="not one of"):
        await oracle.check("item-1", "something", ALICE)


# ── And none of it reaches the logs ──────────────────────────────────────────


async def test_neither_the_answer_nor_the_claim_is_logged(caplog: pytest.LogCaptureFixture) -> None:
    """A log is a context too. The oracle that carefully withholds the answer from the tutor and
    then writes it to stdout has protected nothing."""
    oracle, _ = _oracle('{"outcome": "wrong", "misconception": "thinks-tf-is-linear"}')

    with caplog.at_level(logging.INFO):
        await oracle.check("item-1", "my private and embarrassing guess", ALICE)

    logged = "\n".join(r.getMessage() + str(vars(r)) for r in caplog.records)

    assert THE_ANSWER not in logged
    assert "saturates term frequency" not in logged
    assert "embarrassing" not in logged

    # But the verdict *is* recorded — it is what the Twin will act on, and it is three words the
    # learner already knows.
    assert "thinks-tf-is-linear" in logged
