"""**A question they have never seen, and an answer they never will.**

The generating model produces both in one breath — it cannot write a good question without knowing
what a good answer looks like. The instant afterwards those two things must part company forever,
and these tests are about that parting.

The second half is about the word "never-seen", which is doing more work than it looks. Models
repeat themselves. A model told to "make it novel" will change the names and the numbers and hand
back the same problem, entirely sincerely. So novelty here is a **hash**, not a hope.
"""

from __future__ import annotations

import json
import logging
from dataclasses import fields
from typing import Any

import pytest

from praxis_ai.claude import ClaudeClient
from praxis_ai.config import Settings
from praxis_ai.cost import Attribution
from praxis_ai.items import (
    CAN_CROSS_THE_HOLD_THRESHOLD,
    EvidenceType,
    Item,
    ItemGenerator,
    ItemRejectedError,
    fingerprint_of,
)
from praxis_ai.models import Task, model_for
from praxis_ai.oracle import InMemoryAnswerKeyStore

ALICE = Attribution(learner_id="alice", concept_id="bm25")

THE_ANSWER = "Because term frequency saturates: the tenth occurrence adds far less than the second."

QUESTION = "A document repeats 'jaguar' 60 times. Why does BM25 not rank it 6x above one with 10?"


def _reply(question: str = QUESTION, answer: str = THE_ANSWER, **misconceptions: str) -> str:
    return json.dumps(
        {
            "question": question,
            "answer": answer,
            "misconceptions": misconceptions or {"thinks-tf-is-linear": "TF contributes linearly."},
        }
    )


class _Model:
    def __init__(self, replies: list[str]) -> None:
        self.replies = replies
        self.calls: list[dict[str, Any]] = []

        outer = self

        class _Messages:
            async def create(self, **kwargs: Any) -> Any:
                index = min(len(outer.calls), len(outer.replies) - 1)
                outer.calls.append(kwargs)

                return _Response(outer.replies[index])

        self.messages = _Messages()


class _Response:
    def __init__(self, text: str) -> None:
        self.content = [type("Block", (), {"type": "text", "text": text})()]
        self.usage = type("Usage", (), {"input_tokens": 300, "output_tokens": 200})()
        self.stop_reason = "end_turn"


def _generator(*replies: str) -> tuple[ItemGenerator, InMemoryAnswerKeyStore, _Model]:
    model = _Model(list(replies))
    store = InMemoryAnswerKeyStore()

    client = ClaudeClient(Settings(anthropic_api_key="sk-ant-not-real"), client=model)

    return ItemGenerator(client, store), store, model


async def _generate(generator: ItemGenerator, **kwargs: Any) -> Item:
    return await generator.generate(
        concept_id="bm25",
        concept_summary="BM25 saturates term frequency and normalises for length.",
        evidence_type=EvidenceType.APPLY,
        difficulty=0.6,
        attribution=ALICE,
        **kwargs,
    )


# ── The parting of the ways ──────────────────────────────────────────────────


async def test_the_item_comes_back_and_the_answer_does_not() -> None:
    """**The whole milestone, in one assertion.**"""
    generator, store, _ = _generator(_reply())

    item = await _generate(generator)

    assert item.stem == QUESTION

    # The answer is nowhere on the object that goes to the tutor. Not in a field, not in the repr,
    # not hiding in the id.
    assert THE_ANSWER not in str(item)
    assert THE_ANSWER not in repr(item)

    # But it *does* exist — filed with the oracle, which is the only thing that can read it.
    assert store.get(item.id).answer == THE_ANSWER


def test_there_is_no_field_on_an_item_that_could_hold_an_answer() -> None:
    """The guarantee you can check by reading the type.

    No ``answer``. No ``solution``. No ``worked_example`` — not even a nullable one "for the review
    screen after they've finished", because the moment the field exists, something populates it, and
    the tutor is a program that can read its own arguments.
    """
    names = {f.name for f in fields(Item)}

    forbidden = {"answer", "answer_key", "solution", "expected", "worked_example", "rubric"}

    assert not (names & forbidden), f"An Item can now carry {names & forbidden}."

    assert names == {"id", "concept_id", "evidence_type", "stem", "difficulty", "fingerprint"}


async def test_the_answer_key_is_filed_with_the_misconceptions_it_catches() -> None:
    generator, store, _ = _generator(_reply(**{"confuses-tf-with-idf": "Blames IDF."}))

    item = await _generate(generator)

    key = store.get(item.id)

    assert "confuses-tf-with-idf" in key.misconceptions

    # And those keys are the *only* vocabulary the oracle can ever answer with (M5.3).
    assert key.concept_id == "bm25"


# ── Never-seen means never seen ──────────────────────────────────────────────


async def test_an_item_the_learner_has_already_met_is_refused_not_served() -> None:
    """**Asking a model for novelty is not a guarantee. Hashing the stem is.**

    I-2 requires APPLY evidence to come from a genuinely never-seen problem. An APPLY item the
    learner has met before is not evidence of anything — it is a recall item with a false label on
    it, and every conclusion PRAXIS draws from it is fraudulent.
    """
    generator, _, model = _generator(_reply())   # the model returns the same item every time

    seen = frozenset({fingerprint_of(QUESTION)})

    with pytest.raises(ItemRejectedError, match="already seen"):
        await _generate(generator, already_seen=seen)

    # It genuinely tried again rather than giving up on the first collision.
    assert len(model.calls) == 3


async def test_a_model_that_only_changes_the_cosmetics_does_not_fool_the_check() -> None:
    """The realistic failure. Told to "make it novel", a model rewrites the punctuation and the
    capitalisation and considers the job done."""
    original = "Why does BM25 not rank a 60-occurrence document 6x above a 10-occurrence one?"

    cosmetic = "why does BM25 NOT rank a 60-occurrence document 6x above a 10-occurrence one???"

    assert fingerprint_of(original) == fingerprint_of(cosmetic), (
        "A model changed the punctuation and PRAXIS called it a new question."
    )


async def test_a_genuinely_different_item_is_served() -> None:
    """The other half. The check must not be so strict that nothing is ever novel."""
    fresh = "A 3-word query matches a 10,000-word document. Why does BM25 penalise it?"

    generator, store, _ = _generator(_reply(question=fresh))

    item = await _generate(generator, already_seen=frozenset({fingerprint_of(QUESTION)}))

    assert item.stem == fresh
    assert store.get(item.id) is not None


async def test_the_second_attempt_is_served_when_the_first_collides() -> None:
    generator, _, model = _generator(_reply(), _reply(question="Something genuinely different?"))

    item = await _generate(generator, already_seen=frozenset({fingerprint_of(QUESTION)}))

    assert item.stem == "Something genuinely different?"
    assert len(model.calls) == 2


# ── Refusals ─────────────────────────────────────────────────────────────────


async def test_an_item_with_no_answer_key_is_refused() -> None:
    """It would reach a learner, they would spend real effort on it, and the oracle would have
    nothing to mark it against — so their work would be silently discarded and they would never
    find out why."""
    generator, _, _ = _generator(json.dumps({"question": QUESTION, "answer": "  "}))

    with pytest.raises(ItemRejectedError, match="nothing to say back"):
        await _generate(generator)


async def test_an_unparseable_reply_is_refused_rather_than_shown() -> None:
    generator, _, _ = _generator("Here's a great question for you!")

    with pytest.raises(ItemRejectedError):
        await _generate(generator)


async def test_a_difficulty_outside_the_scale_is_a_bug_and_says_so() -> None:
    generator, _, _ = _generator(_reply())

    with pytest.raises(ValueError, match="not a difficulty"):
        await generator.generate(
            concept_id="bm25",
            concept_summary="...",
            evidence_type=EvidenceType.APPLY,
            difficulty=1.4,
            attribution=ALICE,
        )


# ── Bias: the writer must not be the marker ──────────────────────────────────


def test_the_model_that_writes_the_item_is_not_the_model_that_grades_it() -> None:
    """**Self-preference is a measured LLM-judge failure mode.**

    A model asked to grade an answer to a question *it wrote* marks generously, because it
    recognises its own framing. Here the separation is free — item generation is a fast-tier task
    and grading is a heavy-tier one — but it is free by *accident*, so it gets a test, because the
    day someone equalises the tiers for a good reason this stops being true silently.
    """
    settings = Settings()

    assert model_for(Task.GENERATE_ITEM, settings) != model_for(Task.GRADE, settings)


# ── The Ceiling, restated where the items are made ───────────────────────────


def test_the_evidence_types_that_can_confer_mastery_are_the_unfakeable_ones() -> None:
    """C-4, mirrored from ``Praxis.Shared.EvidenceTypes``. If these two ever disagree, one of them
    is lying to a learner about what they have proved."""
    assert CAN_CROSS_THE_HOLD_THRESHOLD == {
        EvidenceType.APPLY,
        EvidenceType.BUILD,
        EvidenceType.PREDICT,
    }

    assert EvidenceType.EXPLAIN not in CAN_CROSS_THE_HOLD_THRESHOLD
    assert EvidenceType.RECALL not in CAN_CROSS_THE_HOLD_THRESHOLD
    assert EvidenceType.CRITIQUE not in CAN_CROSS_THE_HOLD_THRESHOLD


# ── The muzzle ───────────────────────────────────────────────────────────────


async def test_the_answer_is_not_logged_when_the_item_is_made(
    caplog: pytest.LogCaptureFixture,
) -> None:
    """The generator holds the answer for a few microseconds. That is long enough to log it."""
    generator, _, _ = _generator(_reply())

    with caplog.at_level(logging.INFO):
        await _generate(generator)

    logged = "\n".join(r.getMessage() + str(vars(r)) for r in caplog.records)

    assert THE_ANSWER not in logged
    assert "saturates" not in logged
