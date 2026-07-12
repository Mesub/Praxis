"""**The grader's most important output is "I don't know".**

Every LLM grader ever shipped will return a number for anything. Hand it a blank page, a poem, or an
argument that is subtly right in a way the rubric's author never anticipated, and it will hand back
`0.73` with exactly the confidence it brings to the easy cases.

That number becomes Evidence. Evidence moves what PRAXIS believes about a person. A confidently
wrong grade is not a bad row in a table — it is a false belief about someone's mind, and they will
meet it months later as work that is inexplicably too hard, and they will blame themselves.

So: `PENDING` is a real answer, and these tests exist to make sure it can never be quietly turned
into a `PASSED` by a long queue and a good intention.
"""

from __future__ import annotations

import json
from decimal import Decimal
from typing import Any

import pytest

from praxis_ai.claude import ClaudeClient
from praxis_ai.config import Settings
from praxis_ai.cost import Attribution
from praxis_ai.grading import BOUNDARY, Grade, Grader, Outcome
from praxis_ai.rubric import Dimension, HardFail, Rubric

ALICE = Attribution(learner_id="alice", concept_id="support-resistance")

RUBRIC = Rubric(
    id="rub.sr.explain.v2",
    concept_id="support-resistance",
    audience="a smart friend who has never traded",
    pass_score=0.75,
    dimensions=(
        Dimension(
            id="mechanism",
            weight=0.5,
            probe="Do they explain WHY price reacts there, or only THAT it does?",
            bands={
                4: "Names resting limit orders absorbing flow AND explains depletion.",
                3: "Supply/demand imbalance, correctly, but no order-book mechanism.",
                2: "Descriptive only.",
                1: "Restates a definition.",
                0: "Absent, or asserts a false mechanism.",
            },
        ),
        Dimension(
            id="invalidation",
            weight=0.5,
            probe="Do they state what would make them wrong?",
            bands={
                4: "A specific, checkable invalidation condition.",
                3: "States an invalidation condition.",
                2: "Gestures at 'if it breaks'.",
                1: "None.",
                0: "Asserts it cannot break.",
            },
            auto_flag=(1, "M-SR-004"),
        ),
    ),
    hard_fails=(
        HardFail(
            pattern=r"(cannot|can't|will not|won't|never) (go|fall|drop|close) below",
            misconception="M-SR-004",
            cap=0.40,
        ),
    ),
)

GOOD = (
    "Support is a price area where resting buy orders absorb arriving sells. It holds only while "
    "that pile is refilled faster than it is spent, and it breaks on the touch that empties it."
)


class _Model:
    """A model that returns a scripted reply per call, and counts how often it was consulted."""

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
        self.usage = type("Usage", (), {"input_tokens": 800, "output_tokens": 60})()
        self.stop_reason = "end_turn"


def _grader(*replies: str) -> tuple[Grader, _Model]:
    model = _Model(list(replies))

    client = ClaudeClient(Settings(anthropic_api_key="sk-ant-not-real"), client=model)

    return Grader(client), model


def _bands(mechanism: int, invalidation: int) -> str:
    return json.dumps({"bands": {"mechanism": mechanism, "invalidation": invalidation}})


ABSTAIN = json.dumps({"abstain": True})


# ── Determinism first ────────────────────────────────────────────────────────


async def test_a_hard_fail_phrase_is_settled_without_asking_a_language_model() -> None:
    """**Where truth is deterministic, we use determinism.**

    "The stock can't fall below it" is wrong in a way that needs no opinion. Handing it to a judge
    invites the judge to be *talked round* by the eloquence wrapped around it — and half the
    AI-grading disasters in this industry are exactly that. A regex cannot be flattered.
    """
    grader, model = _grader(_bands(4, 4))

    grade = await grader.grade(
        "Support is the floor. The stock can't fall below it, because buyers defend it with "
        "beautifully described resting liquidity and absorption and depletion.",
        RUBRIC,
        ALICE,
    )

    assert grade.outcome is Outcome.FAILED
    assert grade.score == 0.40
    assert "M-SR-004" in grade.misconceptions

    # And **no judge was consulted at all**. Not one call.
    assert model.calls == []
    assert grade.judges_consulted == 0
    assert grade.usd == Decimal(0)


async def test_something_too_short_to_be_an_explanation_costs_nothing() -> None:
    grader, model = _grader(_bands(4, 4))

    grade = await grader.grade("dunno", RUBRIC, ALICE)

    assert grade.outcome is Outcome.FAILED
    assert model.calls == []


# ── The judges ───────────────────────────────────────────────────────────────


async def test_three_judges_who_agree_produce_a_grade() -> None:
    grader, model = _grader(_bands(4, 4))

    grade = await grader.grade(GOOD, RUBRIC, ALICE)

    assert grade.outcome is Outcome.PASSED
    assert grade.score == 1.0
    assert grade.judges_consulted == 3
    assert len(model.calls) == 3
    assert grade.escalated_to_adjudicator is False


async def test_a_judge_never_learns_who_it_is_marking() -> None:
    """**A judge that knows it is marking a struggling learner's fourth attempt is no longer marking
    the work.**

    Sycophancy and anchoring are measured, reproducible LLM-judge failure modes. The defence is not
    a polite instruction — it is that the identity is *not in the prompt*, because the function that
    builds the prompt was never given it.
    """
    grader, model = _grader(_bands(3, 3))

    await grader.grade(GOOD, RUBRIC, ALICE)

    for call in model.calls:
        prompt = call["messages"][0]["content"] + str(call.get("system", ""))

        assert "alice" not in prompt.lower()
        assert "attempt" not in prompt.lower()
        assert "previous" not in prompt.lower()


async def test_the_judges_do_not_all_read_the_rubric_in_the_same_order() -> None:
    """Position bias is real and measured. Three judges reading one list in one order share it."""
    grader, model = _grader(_bands(3, 3))

    await grader.grade(GOOD, RUBRIC, ALICE)

    firsts = [
        call["messages"][0]["content"].index("mechanism")
        < call["messages"][0]["content"].index("invalidation")
        for call in model.calls
    ]

    assert len(set(firsts)) > 1, "Every judge read the dimensions in the same order."


# ── Abstention ───────────────────────────────────────────────────────────────


async def test_when_the_judges_will_not_band_it_praxis_does_not_either() -> None:
    """**Abstention is contagious, and that is correct.**

    If two of three judges say "I cannot band this", the honest reading is that the *rubric* did not
    anticipate this response — not that the one judge willing to guess happened to be right.
    """
    grader, _ = _grader(ABSTAIN, ABSTAIN, _bands(4, 4))

    grade = await grader.grade(GOOD, RUBRIC, ALICE)

    assert grade.outcome is Outcome.PENDING
    assert grade.escalated_to_human is True

    # No score at all. A PENDING with a confident-looking number attached invites someone to use it.
    assert grade.score is None

    assert "will not average a guess" in grade.reason


async def test_a_judge_that_cannot_be_parsed_has_abstained_and_has_not_scored_zero() -> None:
    """A parse failure is *our* bug. Charging it to the learner as a zero is the single most
    contemptible thing this module could do."""
    grader, _ = _grader("I think this is quite good, honestly!", ABSTAIN, _bands(4, 4))

    grade = await grader.grade(GOOD, RUBRIC, ALICE)

    assert grade.outcome is Outcome.PENDING
    assert grade.score != 0.0


async def test_a_judge_that_invents_a_band_outside_the_scale_has_abstained() -> None:
    """A judge returning band 7, or skipping a dimension, has not graded this response — and does
    not get the parts it *did* fill in averaged into a number that looks legitimate."""
    grader, _ = _grader(
        json.dumps({"bands": {"mechanism": 7, "invalidation": 4}}),
        json.dumps({"bands": {"mechanism": 3}}),   # skipped a dimension
        _bands(4, 4),
    )

    grade = await grader.grade(GOOD, RUBRIC, ALICE)

    assert grade.outcome is Outcome.PENDING


# ── Escalation ───────────────────────────────────────────────────────────────


async def test_judges_more_than_half_a_band_apart_go_to_an_adjudicator() -> None:
    grader, model = _grader(_bands(4, 4), _bands(1, 1), _bands(4, 3), _bands(4, 4))

    grade = await grader.grade(GOOD, RUBRIC, ALICE)

    assert grade.escalated_to_adjudicator is True
    assert len(model.calls) == 4   # three judges, then the adjudicator

    # The adjudicator ran on the heavy model, and it saw what the judges said.
    assert model.calls[3]["model"] == "claude-opus-4-8"
    assert "DISAGREED" in model.calls[3]["messages"][0]["content"]


async def test_an_adjudicator_that_abstains_sends_it_to_a_human() -> None:
    grader, _ = _grader(_bands(4, 4), _bands(0, 0), _bands(4, 4), ABSTAIN)

    grade = await grader.grade(GOOD, RUBRIC, ALICE)

    assert grade.outcome is Outcome.PENDING
    assert grade.escalated_to_human is True
    assert "declined to break the tie" in grade.reason


# ── The boundary. The one that matters. ──────────────────────────────────────


async def test_a_score_on_the_threshold_is_pending_and_not_a_pass() -> None:
    """**The only case where a grade changes the decision, and therefore the only case worth a
    human's time.**

    Bands 3 and 3 give exactly 0.75 — the contract threshold to four decimal places. A grader that
    rounds this to PASSED has decided a person's mastery on a coin-flip and written it into a Ledger
    that claims to mean something.
    """
    grader, _ = _grader(_bands(3, 3))

    grade = await grader.grade(GOOD, RUBRIC, ALICE)

    assert grade.score == pytest.approx(0.75)

    assert grade.outcome is Outcome.PENDING, (
        "A score exactly on the contract threshold was resolved by arithmetic. PRAXIS has just "
        "decided somebody's mastery on a rounding rule."
    )

    assert grade.escalated_to_human is True

    # The score *is* carried here — a human needs it — but the decision is not made.
    assert grade.score is not None


async def test_a_clear_pass_is_not_sent_to_a_human() -> None:
    """The other half of the argument. Routing everything to a human is not caution — it is a
    product that cannot run, and it helps no learner at all."""
    grader, _ = _grader(_bands(4, 4))

    grade = await grader.grade(GOOD, RUBRIC, ALICE)

    assert grade.outcome is Outcome.PASSED
    assert grade.escalated_to_human is False


async def test_a_clear_fail_is_not_sent_to_a_human() -> None:
    grader, _ = _grader(_bands(1, 1))

    grade = await grader.grade(GOOD, RUBRIC, ALICE)

    assert grade.outcome is Outcome.FAILED
    assert grade.escalated_to_human is False


def test_the_boundary_is_narrow_enough_to_be_affordable() -> None:
    """If every borderline case goes to a person, PRAXIS is a mechanical turk with a graph. The
    band is ±0.05 — a few percent of responses, which is what a human's attention is worth."""
    assert BOUNDARY == 0.05


# ── PENDING can never be coerced ─────────────────────────────────────────────


def test_a_grade_cannot_be_passed_and_awaiting_a_human_at_once() -> None:
    """**The coercion this whole state exists to prevent.**

    One day the review queue will be long, the metrics will look bad, and letting the borderline
    cases through will be reasonable, temporary, and 'probably fine'. It is not fine: it converts
    "PRAXIS does not know" into "PRAXIS certifies", silently, on the hardest cases to call.

    The type refuses to represent it.
    """
    with pytest.raises(ValueError, match="cannot be PASSED and awaiting a human"):
        Grade(
            outcome=Outcome.PASSED,
            score=0.751,
            misconceptions=(),
            reason="the queue was long and it was probably fine",
            judges_consulted=3,
            escalated_to_adjudicator=False,
            escalated_to_human=True,
            usd=Decimal("0.01"),
        )


def test_pending_is_its_own_outcome_and_not_a_flavour_of_failed() -> None:
    """A learner whose work is PENDING has not failed. They are waiting on *us*."""
    assert Outcome.PENDING is not Outcome.FAILED
    assert {o.value for o in Outcome} == {"passed", "failed", "pending"}


# ── Cost ─────────────────────────────────────────────────────────────────────


async def test_one_unpriced_leg_poisons_the_whole_bill() -> None:
    """A sum that treats an unknown component as zero is a number that lies with a straight face."""
    model = _Model([_bands(4, 4)])

    settings = Settings(
        anthropic_api_key="sk-ant-not-real", model_heavy="claude-nobody-priced-this"
    )

    grade = await Grader(ClaudeClient(settings, client=model)).grade(GOOD, RUBRIC, ALICE)

    assert grade.usd is None, "An unpriced grading call was silently costed at zero."
