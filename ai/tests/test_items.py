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
    ItemFormat,
    ItemGenerator,
    ItemRejectedError,
    fingerprint_of,
)
from praxis_ai.models import Task, model_for
from praxis_ai.oracle import InMemoryAnswerKeyStore

ALICE = Attribution(learner_id="alice", concept_id="bm25")

THE_ANSWER = "Because term frequency saturates: the tenth occurrence adds far less than the second."

QUESTION = "A document repeats 'jaguar' 60 times. Why does BM25 not rank it 6x above one with 10?"

PASSAGE = (
    "The new roster system goes live on Monday. Everyone who worked a Saturday shift last month "
    "has already been moved across. However, if you swapped a Saturday with a colleague and did "
    "not tell the office, the system has no record of it."
)

MCQ_QUESTION = "Whose roster is the system most likely to get wrong?"

MCQ_OPTIONS = (
    "Everyone who worked a Saturday shift last month.",
    "Anyone who swapped a shift without telling the office.",
    "All warehouse staff.",
    "Anyone who calls the front desk.",
)


def _reply(question: str = QUESTION, answer: str = THE_ANSWER, **misconceptions: str) -> str:
    return json.dumps(
        {
            "question": question,
            "answer": answer,
            "misconceptions": misconceptions or {"thinks-tf-is-linear": "TF contributes linearly."},
        }
    )


def _mcq_reply(
    *,
    passage: str = PASSAGE,
    question: str = MCQ_QUESTION,
    options: tuple[str, ...] = MCQ_OPTIONS,
    correct: int = 1,
    option_misconceptions: dict[str, str] | None = None,
    misconceptions: dict[str, str] | None = None,
    targets: str | None = None,
) -> str:
    return json.dumps(
        {
            "passage": passage,
            "question": question,
            "options": list(options),
            "correct": correct,
            "option_misconceptions": {} if option_misconceptions is None else option_misconceptions,
            "answer": options[correct],
            "misconceptions": {} if misconceptions is None else misconceptions,
            "targets": targets,
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

    assert names == {
        "id",
        "concept_id",
        "evidence_type",
        "stem",
        "difficulty",
        "passage",
        "options",
        "fingerprint",
        "targets_misconception",
    }


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


# ── The generator is handed the declared wrong beliefs ───────────────────────


async def test_the_generator_is_handed_the_declared_wrong_beliefs_and_targets_one() -> None:
    """The reply names a declared key; it survives to the item. Free-text slugs never do."""
    generator, store, _model = _generator(
        json.dumps(
            {
                "question": QUESTION,
                "answer": THE_ANSWER,
                "misconceptions": {},
                "targets": "bm25-is-obsolete",
            }
        )
    )

    item = await _generate(
        generator, known_misconceptions={"bm25-is-obsolete": "I think BM25 is obsolete."}
    )

    assert item.targets_misconception == "bm25-is-obsolete"

    # The declared statement rides along in the answer key, so the oracle can recognise the belief
    # at marking time even though the generator's own map was empty this time.
    key = store.get(item.id)
    assert key.misconceptions["bm25-is-obsolete"] == "I think BM25 is obsolete."


async def test_the_generators_own_statement_of_the_targeted_belief_is_kept() -> None:
    """When the generator's own ``misconceptions`` map already restates the targeted belief, that
    statement wins — the declared one is only a fallback for when the map is silent. The
    generator's phrasing is written against *this* item's wrong answer, which is what the oracle
    will actually be marking."""
    generator, store, _model = _generator(
        json.dumps(
            {
                "question": QUESTION,
                "answer": THE_ANSWER,
                "misconceptions": {"bm25-is-obsolete": "Believes TF-IDF superseded BM25 here."},
                "targets": "bm25-is-obsolete",
            }
        )
    )

    item = await _generate(
        generator, known_misconceptions={"bm25-is-obsolete": "I think BM25 is obsolete."}
    )

    key = store.get(item.id)
    assert key.misconceptions["bm25-is-obsolete"] == "Believes TF-IDF superseded BM25 here."


async def test_an_undeclared_target_is_dropped_never_invented() -> None:
    generator, _store, _model = _generator(
        json.dumps(
            {
                "question": QUESTION,
                "answer": THE_ANSWER,
                "misconceptions": {},
                "targets": "made-up-slug",
            }
        )
    )

    item = await _generate(
        generator, known_misconceptions={"bm25-is-obsolete": "I think BM25 is obsolete."}
    )

    assert item.targets_misconception is None


async def test_no_declared_beliefs_means_no_target_and_no_prompt_section() -> None:
    generator, _store, model = _generator(
        json.dumps({"question": QUESTION, "answer": THE_ANSWER, "misconceptions": {}})
    )

    item = await _generate(generator)

    assert item.targets_misconception is None

    prompt = model.calls[-1]["messages"][0]["content"]
    assert "KNOWN WRONG BELIEFS" not in prompt  # capture the prompt the way the existing tests in
    # this file do — via the fake model's recorded call kwargs, not a second fake client.


# ── The fixed-key shape: a passage, four options, and the index that never returns ──


async def test_a_multiple_choice_item_carries_a_passage_and_options() -> None:
    generator, _store, _model = _generator(
        _mcq_reply(
            option_misconceptions={"0": "keyword-match-wins", "2": "literal-only"},
            misconceptions={
                "keyword-match-wins": "I think the option repeating the text is right."
            },
            targets="keyword-match-wins",
        )
    )

    item = await _generate(
        generator,
        item_format=ItemFormat.MULTIPLE_CHOICE,
        known_misconceptions={"keyword-match-wins": "...", "literal-only": "..."},
    )

    assert item.passage is not None and "However" in item.passage
    assert item.options == MCQ_OPTIONS


async def test_the_correct_index_goes_to_the_store_and_not_to_the_caller() -> None:
    """**The whole milestone, restated for the fixed-key shape.**"""
    generator, store, _model = _generator(
        _mcq_reply(
            option_misconceptions={"0": "keyword-match-wins", "2": "literal-only"},
            misconceptions={
                "keyword-match-wins": "I think the option repeating the text is right."
            },
            targets="keyword-match-wins",
        )
    )

    item = await _generate(
        generator,
        item_format=ItemFormat.MULTIPLE_CHOICE,
        known_misconceptions={"keyword-match-wins": "...", "literal-only": "..."},
    )

    # The type has nowhere to put it, so this is belt and braces on the type's promise. ``Item`` is
    # a slots dataclass, so ask ``dataclasses.fields`` — ``vars()`` raises on slots classes.
    assert not any("correct" in f.name for f in fields(item))

    key = store.get(item.id)
    assert key is not None
    assert key.correct_option == 1
    assert key.option_misconceptions == {0: "keyword-match-wins", 2: "literal-only"}


async def test_a_distractor_belief_outside_the_declared_set_is_discarded() -> None:
    """The airlock, again: a distractor may only name a belief the caller declared in advance."""
    generator, store, _model = _generator(
        _mcq_reply(
            option_misconceptions={"0": "keyword-match-wins", "2": "invented-on-the-spot"},
            misconceptions={
                "keyword-match-wins": "I think the option repeating the text is right."
            },
            targets="keyword-match-wins",
        )
    )

    item = await _generate(
        generator,
        item_format=ItemFormat.MULTIPLE_CHOICE,
        known_misconceptions={"keyword-match-wins": "..."},
    )

    assert store.get(item.id).option_misconceptions == {0: "keyword-match-wins"}


async def test_a_multiple_choice_item_without_four_options_is_rejected() -> None:
    # Three options, still valid JSON — so this fails the option-count rule, not the parser.
    generator, _store, _model = _generator(_mcq_reply(options=MCQ_OPTIONS[:3]))

    with pytest.raises(ItemRejectedError):
        await _generate(generator, item_format=ItemFormat.MULTIPLE_CHOICE)


async def test_a_multiple_choice_item_with_no_declared_misconceptions_still_generates() -> None:
    """**A fixed-key item may have no declared misconceptions at all.**

    Only 8 of the 15 Reading English concepts declare any — the rest still ship as
    ``MULTIPLE_CHOICE``. A distractor with no named belief behind it is a supported, designed case:
    it marks "wrong" and says nothing more. Neither the prompt nor the parser may require a
    non-empty belief list.
    """
    generator, store, _model = _generator(_mcq_reply(option_misconceptions={}, misconceptions={}))

    item = await _generate(
        generator, item_format=ItemFormat.MULTIPLE_CHOICE, known_misconceptions={}
    )

    assert item.options == MCQ_OPTIONS

    key = store.get(item.id)
    assert key is not None
    assert key.correct_option == 1
    assert key.option_misconceptions == {}


def test_a_prose_fingerprint_is_unchanged_by_the_passage_field() -> None:
    """Every item already banked must keep the fingerprint it was banked with, or ``already_seen``
    stops meaning anything and the bank fills with duplicates."""
    assert fingerprint_of("What does 'however' do?") == fingerprint_of(
        "What does 'however' do?", passage=None
    )


def test_two_questions_about_the_same_passage_do_not_collide_and_two_identical_ones_do() -> None:
    passage = "A short passage about a roster."

    assert fingerprint_of("Who acts?", passage=passage) != fingerprint_of("When?", passage=passage)
    assert fingerprint_of("Who acts?", passage=passage) == fingerprint_of(
        "Who acts?", passage=passage
    )
    assert fingerprint_of("Who acts?", passage=passage) != fingerprint_of("Who acts?")


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
