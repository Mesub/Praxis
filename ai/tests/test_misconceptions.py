"""**Turning a wrong answer into a bug report on a human mind.**

The obvious question to ask a model is *"what did they get wrong?"*, and the answer is useless
every time: *"they said support levels cannot be broken."* That is a **restatement of the error**.
You cannot teach against it, you cannot build an item from it, and you cannot watch it die.

PRAXIS asks a different question: **"what RULE would PRODUCE this answer?"** — and gets back *"they
believe a price level is a physical barrier rather than a finite pool of liquidity."*

**The first is an error. The second is a bug report on a human mind.** Only the second is
targetable.

The entire misconception engine (I-6) rests on asking the second question rather than the first, so
the tests below are mostly about the *prompt*.
"""

from __future__ import annotations

import json
from typing import Any

import pytest

from praxis_ai.claude import ClaudeClient
from praxis_ai.config import Settings
from praxis_ai.cost import Attribution
from praxis_ai.misconceptions import (
    MIN_LEARNERS_TO_PROMOTE,
    Extraction,
    KnownMisconception,
    MisconceptionExtractor,
    NotAMisconceptionError,
    can_promote,
)

ALICE = Attribution(learner_id="alice", concept_id="support-resistance")

LIBRARY = [
    KnownMisconception(
        "levels-as-walls",
        "A price level is a physical barrier that price cannot pass through.",
    ),
    KnownMisconception(
        "touch-count-strength",
        "The more times a level holds, the stronger it becomes.",
    ),
]

THE_ANSWER = "Support is where resting liquidity absorbs arriving sell orders until it is spent."


class _Model:
    def __init__(self, reply: str) -> None:
        self.calls: list[dict[str, Any]] = []
        outer = self

        class _Messages:
            async def create(self, **kwargs: Any) -> Any:
                outer.calls.append(kwargs)
                return _Response(outer.reply)

        self.reply = reply
        self.messages = _Messages()


class _Response:
    def __init__(self, text: str) -> None:
        self.content = [type("B", (), {"type": "text", "text": text})()]
        self.usage = type("U", (), {"input_tokens": 300, "output_tokens": 60})()
        self.stop_reason = "end_turn"


def _extractor(reply: str) -> tuple[MisconceptionExtractor, _Model]:
    model = _Model(reply)

    client = ClaudeClient(Settings(anthropic_api_key="sk-ant-not-real"), client=model)

    return MisconceptionExtractor(client), model


async def _extract(
    extractor: MisconceptionExtractor, wrote: str = "It can't go below."
) -> Extraction:
    return await extractor.extract(
        question="Why does price sometimes fall through a support level?",
        their_answer=wrote,
        known=LIBRARY,
        attribution=ALICE,
    )


# ── The prompt is the trick ──────────────────────────────────────────────────


async def test_the_extractor_asks_what_rule_would_produce_this_not_what_they_got_wrong() -> None:
    """**The whole engine rests on this one question.**

    *"What did they get wrong?"* yields a restatement of the error. *"What rule would produce this
    answer?"* yields a belief you can build an item against and watch die.
    """
    extractor, model = _extractor(
        json.dumps({
            "rule": "A level is a wall.",
            "key": "levels-as-walls",
            "no_misconception": False,
        })
    )

    await _extract(extractor)

    system = model.calls[0]["system"]

    assert "what RULE would PRODUCE this answer" in system

    # And it is explicitly told that the obvious question is the wrong one.
    assert "Do not tell me what the learner got wrong" in system
    assert "it does not explain it" in system


async def test_the_correct_answer_is_never_given_to_the_extractor() -> None:
    """It reasons **forwards** — from the learner's answer to the belief that generated it — not
    backwards from the truth. So it does not need the key, and keeping it out means one fewer place
    in the world where the answer exists (M5.3)."""
    extractor, model = _extractor(json.dumps({"rule": "A level is a wall.", "key": None}))

    await _extract(extractor)

    everything = json.dumps(model.calls, default=str)

    assert THE_ANSWER not in everything
    assert "answer_key" not in everything


# ── Matching the declared library ────────────────────────────────────────────


async def test_a_known_misconception_is_matched_to_its_key() -> None:
    extractor, _ = _extractor(
        json.dumps({"rule": "Price cannot pass a level.", "key": "levels-as-walls"})
    )

    extraction = await _extract(extractor)

    assert extraction.key == "levels-as-walls"
    assert extraction.is_known
    assert not extraction.is_learner_local


async def test_an_invented_key_is_refused_and_treated_as_learner_local() -> None:
    """**The closed-vocabulary discipline.**

    A model inventing keys fragments the library into synonyms — `levels-as-walls`,
    `level_is_a_wall`, `barrier-thinking` — and a misconception that cannot be *matched* across
    learners cannot be mined, named, or built into an item bank.
    """
    extractor, _ = _extractor(
        json.dumps({"rule": "A level is a wall.", "key": "barrier-thinking-v2"})
    )

    extraction = await _extract(extractor)

    assert extraction.key is None
    assert extraction.is_learner_local

    # The *rule* survives — PRAXIS can still hunt a belief it has not named globally.
    assert extraction.rule


async def test_a_genuinely_new_belief_is_learner_local_and_immediately_usable() -> None:
    """**We can target a belief we have not named.**

    A candidate is real for the learner who produced it, and it is hunted for them from the moment
    it appears (M7.4). It simply does not become a fact about the world yet.
    """
    extractor, _ = _extractor(
        json.dumps({
            "rule": "Support only counts if the candle body closes below it, not the wick.",
            "key": None,
        })
    )

    extraction = await _extract(extractor)

    assert extraction.is_learner_local
    assert not extraction.is_known
    assert "candle body" in extraction.rule


# ── Sometimes a person is just wrong ─────────────────────────────────────────


async def test_a_slip_is_not_a_misconception() -> None:
    """**A misconception engine that always finds a misconception is a horoscope.**

    Sometimes a person is simply wrong: a slip, a typo, a misread, a bad afternoon. There is no
    coherent wrong *rule* underneath it, and inventing one would mean PRAXIS teaching a learner out
    of a belief **they do not hold** — and then generating items against it, and scheduling sessions
    for it, and spending their evenings on it.
    """
    extractor, _ = _extractor(json.dumps({"rule": "", "no_misconception": True}))

    with pytest.raises(NotAMisconceptionError, match="just wrong"):
        await _extract(extractor, wrote="suport is were price stops")


async def test_an_unparseable_reply_does_not_invent_a_belief() -> None:
    extractor, _ = _extractor("I think they're confused about levels generally.")

    with pytest.raises(NotAMisconceptionError):
        await _extract(extractor)


async def test_an_empty_rule_is_not_a_diagnosis() -> None:
    extractor, _ = _extractor(json.dumps({"rule": "   ", "key": None}))

    with pytest.raises(NotAMisconceptionError):
        await _extract(extractor)


# ── The global library is not polluted by n=1 noise ──────────────────────────


def test_one_learners_belief_is_not_a_fact_about_the_world() -> None:
    """**The global library does not get polluted by n=1 noise.**

    One learner believing something odd is a fact about that learner. It is targeted for them
    immediately — and it does not get a name, a curator, or a slot in the item bank until enough
    other people turn out to hold it too.
    """
    assert not can_promote(distinct_learners=1, coherence=0.99)
    assert not can_promote(distinct_learners=24, coherence=0.99)

    assert can_promote(distinct_learners=MIN_LEARNERS_TO_PROMOTE, coherence=0.75)


def test_a_scattered_cluster_is_not_a_misconception_either() -> None:
    """Twenty-five learners whose "shared" belief does not actually cohere are twenty-five different
    beliefs wearing one label — and an item bank built from it would target none of them."""
    assert not can_promote(distinct_learners=100, coherence=0.4)


def test_a_human_being_still_has_to_approve_it() -> None:
    """**No misconception enters the global library because a model was confident.**"""
    from praxis_ai.misconceptions import REQUIRES_HUMAN_CURATOR

    assert REQUIRES_HUMAN_CURATOR is True


# ── The judgement runs on the heavy model ────────────────────────────────────


async def test_diagnosing_a_learner_is_a_judgement_and_runs_on_the_heavy_model() -> None:
    """This decides something about a person's mind, and what it decides gets taught against and
    scheduled for. It is not a tutor turn (M5.2)."""
    extractor, model = _extractor(json.dumps({"rule": "A level is a wall.", "key": None}))

    await _extract(extractor)

    assert model.calls[0]["model"] == "claude-opus-4-8"
