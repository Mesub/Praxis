"""**Grading somebody's voice without grading their accent.**

The .NET side strips the filler. It cannot strip word order, article use, preposition choice, or
vocabulary range — everything that makes somebody's third language *read* like somebody's third
language. Those survive into the text the judge sees, and a judge that reacts to them has measured
how long a person has spoken English and written the answer into a permanent record of what they
understand.

So PRAXIS does not ask the judge to be fair. **It checks.**
"""

from __future__ import annotations

import json
from typing import Any

import pytest

from praxis_ai.claude import ClaudeClient
from praxis_ai.config import Settings
from praxis_ai.cost import Attribution
from praxis_ai.grading import Grader
from praxis_ai.rubric import Dimension, Rubric
from praxis_ai.teachback import (
    MAX_SURFACE_DIVERGENCE,
    MIN_SURFACE_FORMS,
    BiasCertificate,
    JudgeIsBiasedError,
    SurfaceForm,
    UncertifiedJudgeError,
    certify,
    grade_teach_back,
)

ALICE = Attribution(learner_id="alice", concept_id="inverted-index")

RUBRIC = Rubric(
    id="rub.ii.explain.v1",
    concept_id="inverted-index",
    audience="a smart friend who has never written a search engine",
    # Deliberately clear of 0.75 (band 3) and 1.0 (band 4), so that a judge marking the L2 phrasing
    # down a band shows up here as **divergence** rather than as the grader's boundary escalation.
    # Both refuse — but this file is about the bias probe, so let it be the thing under test.
    pass_score=0.60,
    dimensions=(
        Dimension(
            id="mechanism",
            weight=1.0,
            probe="Do they explain the term-to-document mapping and why it is fast?",
            bands={
                4: "Term to posting list, and why that beats scanning every document.",
                3: "Term to documents, correctly.",
                2: "Descriptive only.",
                1: "Restates the name.",
                0: "Absent or wrong.",
            },
        ),
    ),
    min_length=10,
)

#: **The same understanding, said by four different people.** This is the whole basis of the probe:
#: if the content is identical and the scores are not, the difference is not knowledge.
FORMS = [
    SurfaceForm(
        "native",
        "An inverted index maps each term to the list of documents containing it, so a query reads "
        "one short list instead of scanning every document.",
    ),
    SurfaceForm(
        "l2",
        "The inverted index it maps the term to the documents which are containing it, so the "
        "query is reading one small list and is not scanning all of the documents.",
    ),
    SurfaceForm(
        "terse",
        "Term to posting list. Read one list, not every doc.",
    ),
    SurfaceForm(
        "hedged",
        "I think an inverted index maybe maps each term to the documents that contain it, so I "
        "guess the query only reads one short list rather than scanning everything.",
    ),
]


class _Judge:
    """A judge whose band depends on what you ask it about.

    ``penalise`` is a substring; any response containing it is marked down a band. That is exactly
    what a judge with an accent problem does — and it is invisible unless you look for it.
    """

    def __init__(self, band: int, penalise: str | None = None, penalty: int = 1) -> None:
        self.band = band
        self.penalise = penalise
        self.penalty = penalty
        self.seen: list[str] = []

        outer = self

        class _Messages:
            async def create(self, **kwargs: Any) -> Any:
                text = kwargs["messages"][0]["content"]
                outer.seen.append(text)

                band = outer.band

                if outer.penalise is not None and outer.penalise in text:
                    band = max(0, band - outer.penalty)

                return _Reply(json.dumps({"bands": {"mechanism": band}}))

        self.messages = _Messages()


class _Reply:
    def __init__(self, text: str) -> None:
        self.content = [type("Block", (), {"type": "text", "text": text})()]
        self.usage = type("Usage", (), {"input_tokens": 700, "output_tokens": 40})()
        self.stop_reason = "end_turn"


def _grader(judge: _Judge) -> Grader:
    return Grader(ClaudeClient(Settings(anthropic_api_key="sk-ant-not-real"), client=judge))


# ── The probe catches a judge that marks down an accent ──────────────────────


async def test_a_judge_that_marks_down_non_native_phrasing_is_refused() -> None:
    """**The test this whole module exists for.**

    *"The index it maps the term to the documents which contain it."*
    *"The index maps each term to the documents containing it."*

    **These two people know the same thing.** A judge that gives the second a higher band has
    measured how long they have spoken English — and PRAXIS is about to write that number into a
    permanent record of what they understand.
    """
    biased = _Judge(band=4, penalise="which are containing")

    with pytest.raises(JudgeIsBiasedError) as refused:
        await certify(_grader(biased), RUBRIC, FORMS, ALICE)

    assert "measuring how the learner speaks, not what they know" in str(refused.value)

    # And it names who got hurt, because "the judge is biased" is not actionable and "it marked the
    # L2 phrasing down a band" is.
    assert "'l2'" in str(refused.value)


async def test_a_judge_that_marks_down_hedging_is_refused() -> None:
    """**Penalise hedging and you teach people to sound certain.**

    That is the worst lesson this system could possibly teach, and it is the one every viva and
    every interview in the world teaches by accident. A learner who is unsure *and says so* is doing
    the single most valuable thing in the product (C-6).
    """
    biased = _Judge(band=4, penalise="I guess")

    with pytest.raises(JudgeIsBiasedError):
        await certify(_grader(biased), RUBRIC, FORMS, ALICE)


async def test_a_judge_that_marks_down_brevity_is_refused() -> None:
    """A short correct explanation is a correct explanation. Length is not understanding — it is
    stamina, or a habit of writing essays, and PRAXIS grades neither."""
    # Keyed off a phrase that appears **only in the terse form** — not in the rubric's own band
    # text, which is sent to every judge on every call and would penalise all four forms equally.
    biased = _Judge(band=4, penalise="not every doc")

    with pytest.raises(JudgeIsBiasedError):
        await certify(_grader(biased), RUBRIC, FORMS, ALICE)


async def test_a_judge_that_marks_content_and_not_delivery_is_certified() -> None:
    fair = _Judge(band=4)

    certificate = await certify(_grader(fair), RUBRIC, FORMS, ALICE)

    assert certificate.is_clean
    assert certificate.spread == pytest.approx(0.0)
    assert certificate.forms_tested == len(FORMS)

    # And it kept the receipts, so a human can see what it actually did rather than trusting a bool.
    assert set(certificate.scores) == {"native", "l2", "terse", "hedged"}


async def test_the_tolerance_is_tighter_than_a_rubric_band() -> None:
    """The limit is not zero — judges are stochastic and a band is a coarse instrument. But it is
    far below the gap between two bands, so **no judge can drop somebody a whole band for their
    phrasing and still be certified.**"""
    one_band = 1 / 4  # bands are 0-4, so a single band is a quarter of the scale

    assert MAX_SURFACE_DIVERGENCE < one_band


async def test_one_example_cannot_prove_a_judge_is_fair() -> None:
    with pytest.raises(ValueError, match="at least"):
        await certify(_grader(_Judge(band=4)), RUBRIC, FORMS[:1], ALICE)

    assert MIN_SURFACE_FORMS >= 3


async def test_an_abstention_is_a_hole_in_the_probe_and_not_a_pass() -> None:
    """**The judge does not get to be certified on the examples that went well.**

    A judge that cannot form a view on the L2 phrasing — but is confident about the native one — has
    told you something about itself, and it is not something that should end in a certificate.
    """

    class _Flaky(_Judge):
        def __init__(self) -> None:
            super().__init__(band=4)

            class _Messages:
                async def create(self, **kwargs: Any) -> Any:
                    text = kwargs["messages"][0]["content"]

                    if "which are containing" in text:
                        return _Reply(json.dumps({"abstain": True}))

                    return _Reply(json.dumps({"bands": {"mechanism": 4}}))

            self.messages = _Messages()

    with pytest.raises(JudgeIsBiasedError, match="examples that went well"):
        await certify(_grader(_Flaky()), RUBRIC, FORMS, ALICE)


# ── And an uncertified judge cannot grade at all ─────────────────────────────


async def test_a_teach_back_cannot_be_graded_by_a_judge_nobody_has_checked() -> None:
    """**Not "is discouraged from". Cannot — there is no code path.**

    Telling a model "do not penalise non-native phrasing" in a system prompt is *asking it nicely*,
    and asking a language model nicely is the exact thing this codebase exists to stop relying on.
    """
    with pytest.raises(UncertifiedJudgeError) as refused:
        await grade_teach_back(
            _grader(_Judge(band=4)), FORMS[0].text, RUBRIC, ALICE, certificate=None
        )

    assert "marks content or accent" in str(refused.value)


async def test_a_certificate_that_records_a_failure_is_not_a_licence_to_grade() -> None:
    """The one that would actually happen: somebody runs the probe, it fails, and the certificate
    object is still sitting there — truthfully recording the failure — and gets passed in anyway."""
    failed = BiasCertificate(
        rubric_id=RUBRIC.id,
        forms_tested=4,
        spread=0.25,
        scores={"native": 1.0, "l2": 0.75},
    )

    assert not failed.is_clean

    with pytest.raises(UncertifiedJudgeError, match="not a licence to grade"):
        await grade_teach_back(
            _grader(_Judge(band=4)), FORMS[0].text, RUBRIC, ALICE, certificate=failed
        )


async def test_a_certificate_earned_on_one_rubric_does_not_licence_another() -> None:
    """**Bias is not a property of the model alone — it lives in the rubric's wording too.**

    A rubric whose top band says *"articulates fluently"* will produce a biased judge out of a fair
    model, and a clean run on a different rubric says exactly nothing about it.
    """
    elsewhere = BiasCertificate(
        rubric_id="rub.something.else.v1", forms_tested=4, spread=0.0, scores={}
    )

    with pytest.raises(UncertifiedJudgeError, match="lives in the rubric's wording too"):
        await grade_teach_back(
            _grader(_Judge(band=4)), FORMS[0].text, RUBRIC, ALICE, certificate=elsewhere
        )


async def test_a_certified_judge_grades_the_content() -> None:
    fair = _Judge(band=4)
    grader = _grader(fair)

    certificate = await certify(grader, RUBRIC, FORMS, ALICE)

    grade = await grade_teach_back(grader, FORMS[1].text, RUBRIC, ALICE, certificate)

    assert grade.score == pytest.approx(1.0)


async def test_the_judge_never_learns_how_confident_the_learner_sounded() -> None:
    """The audio does not reach the judge. Neither does the hesitation, nor the ASR's confidence,
    nor how much filler was stripped — because a judge that can see them will react to them."""
    fair = _Judge(band=4)
    grader = _grader(fair)

    certificate = await certify(grader, RUBRIC, FORMS, ALICE)

    await grade_teach_back(grader, FORMS[0].text, RUBRIC, ALICE, certificate)

    for prompt in fair.seen:
        for leak in ("confidence", "disfluen", "hesitat", "stammer", "accent", "audio"):
            assert leak not in prompt.lower(), (
                f"The judge was told about the learner's {leak!r}. It will react to it, because it "
                f"is a language model and it reacts to everything it is shown."
            )
