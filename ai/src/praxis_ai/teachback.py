"""**Grading somebody's voice without grading their accent.**

A teach-back is the most revealing evidence PRAXIS collects and the most dangerous to grade, because
a transcript of speech carries a dozen signals that have **nothing whatever to do with knowing the
thing** — and a language model will cheerfully score every one of them.

The .NET side (`TeachBack.Prepare`) removes what it safely can: filled pauses, stutters, false
starts. **That is not enough**, and pretending it is would be the comfortable lie.

## What survives the stripper

Word order. Article use. Preposition choice. Sentence length. Vocabulary range. Everything that
makes somebody's third language *read* like somebody's third language.

> *"The index it maps the term to the documents which contain it."*
> *"The index maps each term to the documents containing it."*

**These two people know the same thing.** A judge that gives the second a higher band has measured
how long they have spoken English, and has written the result into a permanent record of what they
understand. CANON §5.3 is blunt about this and it is right to be: *a judge that scores L2 English
lower for the same content is demoted, and this one is non-negotiable.*

## So we do not ask the judge to be fair. We check.

Telling a model *"do not penalise non-native phrasing"* in a system prompt is **asking it nicely**,
and asking a language model nicely is the exact thing this codebase exists to stop relying on. The
prompt is still there — it costs nothing and it helps — but it is not the guarantee.

The guarantee is this: **a judge that has not passed the bias probe cannot grade a teach-back.** Not
"is discouraged from". Cannot — there is no code path. `grade_teach_back()` takes a
`BiasCertificate`, and the only way to get one is to have graded the same content in several surface
forms and come out level.

If the judge scores the L2 phrasing lower, PRAXIS does not grade teach-backs today. That is an
outage, and an outage is **enormously** better than quietly marking down every learner who did not
grow up speaking English.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass

from praxis_ai.cost import Attribution
from praxis_ai.grading import Grade, GradeOutcome, Grader
from praxis_ai.rubric import Rubric

log = logging.getLogger("praxis.ai.teachback")

#: How far two surface forms of **identical content** may score apart before the judge is not
#: measuring understanding any more.
#:
#: A tenth of the scale. It is not zero — judges are stochastic and a rubric band is a coarse
#: instrument — but it is far below the gap between two bands, so a judge cannot drop somebody a
#: whole band for their phrasing and still be certified.
MAX_SURFACE_DIVERGENCE = 0.10

#: Below this many surface forms, the probe proves nothing. You cannot detect bias with one example.
MIN_SURFACE_FORMS = 3


class JudgeIsBiasedError(RuntimeError):
    """The judge scored the same content differently depending on how it was said.

    **This is not a bug to work around. It is a judge to stop using.**

    Nothing degrades gracefully here: there is no "grade anyway with a warning", because the warning
    goes into a log that nobody reads and the mark goes into a record of what a person understands.
    """


class UncertifiedJudgeError(RuntimeError):
    """Somebody tried to grade a spoken explanation with a judge nobody has checked.

    The certificate is not paperwork. It is the only evidence PRAXIS has that this judge marks a
    learner on **what they said** rather than on **how they said it**, and it is not the kind of
    thing that can be assumed and audited later — by the time you audit it, you have marked down a
    thousand people.
    """


@dataclass(frozen=True, slots=True)
class SurfaceForm:
    """**The same understanding, said by a different person.**

    Every one of these must express *identical content*. That is the whole basis of the probe: if
    the content is the same and the scores are not, the difference is not knowledge.
    """

    #: What varies — ``"native"``, ``"l2"``, ``"halting"``, ``"terse"``, ``"hedged"``.
    label: str

    text: str


@dataclass(frozen=True, slots=True)
class BiasCertificate:
    """**Proof this judge marks content and not delivery.** Required to grade a teach-back."""

    rubric_id: str
    forms_tested: int
    spread: float
    scores: dict[str, float]

    @property
    def is_clean(self) -> bool:
        return self.spread <= MAX_SURFACE_DIVERGENCE


async def certify(
    grader: Grader,
    rubric: Rubric,
    forms: list[SurfaceForm],
    attribution: Attribution,
) -> BiasCertificate:
    """Grade the same content in several voices and find out whether the judge notices the voice.

    **Raises** `JudgeIsBiasedError` if it does. It does not return a warning, it does not return a
    degraded certificate, and there is no flag to make it continue — because the only thing that
    would ever be done with such a flag is turn it on.
    """
    if len(forms) < MIN_SURFACE_FORMS:
        raise ValueError(
            f"A bias probe needs at least {MIN_SURFACE_FORMS} surface forms. You cannot detect a "
            "judge marking down non-native phrasing by showing it such phrasing once."
        )

    scores: dict[str, float] = {}

    for form in forms:
        grade = await grader.grade(form.text, rubric, attribution)

        if grade.outcome is GradeOutcome.PENDING or grade.score is None:
            # An abstention is not a bias signal, but it *is* a hole in the probe — and a probe with
            # a hole in it must not hand out a certificate. **The judge does not get to be certified
            # on the examples that happened to work.**
            raise JudgeIsBiasedError(
                f"The judges could not agree on the {form.label!r} phrasing of content they agreed "
                "on elsewhere. That disagreement is itself about the phrasing, and PRAXIS is not "
                "going to certify a judge on the strength of the examples that went well."
            )

        scores[form.label] = grade.score

    spread = max(scores.values()) - min(scores.values())

    certificate = BiasCertificate(
        rubric_id=rubric.id,
        forms_tested=len(forms),
        spread=spread,
        scores=scores,
    )

    if not certificate.is_clean:
        worst = min(scores, key=lambda label: scores[label])
        best = max(scores, key=lambda label: scores[label])

        log.error(
            "teach-back judge failed the bias probe",
            extra={"rubric_id": rubric.id, "spread": spread, "penalised": worst},
        )

        raise JudgeIsBiasedError(
            f"The same explanation scored {scores[best]:.2f} as {best!r} and "
            f"{scores[worst]:.2f} as {worst!r} — a spread of {spread:.2f} on identical content. "
            f"This judge is measuring how the learner speaks, not what they know. PRAXIS will not "
            f"grade teach-backs with it, and it will not grade them 'with a warning' either: the "
            f"warning goes in a log nobody reads and the mark goes in a record of what a person "
            f"understands."
        )

    log.info(
        "teach-back judge certified",
        extra={"rubric_id": rubric.id, "forms": len(forms), "spread": spread},
    )

    return certificate


async def grade_teach_back(
    grader: Grader,
    explanation: str,
    rubric: Rubric,
    attribution: Attribution,
    certificate: BiasCertificate | None,
) -> Grade:
    """Grade a spoken explanation.

    ``explanation`` is the **content**, with the speech already taken out of it by
    `TeachBack.Prepare`. The judge never receives the audio, never receives the hesitation, and
    never learns how confident the learner sounded.

    ``certificate`` is not optional in spirit. It is typed as nullable **only so that passing `None`
    is a thing you have to do on purpose**, and when you do, this raises.
    """
    if certificate is None:
        raise UncertifiedJudgeError(
            "This judge has not been probed for bias, so PRAXIS does not know whether it marks "
            "content or accent — and it is not going to find out by marking somebody's teach-back "
            "and seeing what happens."
        )

    if not certificate.is_clean:
        raise UncertifiedJudgeError(
            f"This judge's bias probe came back at {certificate.spread:.2f}, over the "
            f"{MAX_SURFACE_DIVERGENCE:.2f} limit. A certificate that records a failure is not a "
            "licence to grade."
        )

    if certificate.rubric_id != rubric.id:
        raise UncertifiedJudgeError(
            f"This certificate was earned on rubric {certificate.rubric_id!r} and is being used to "
            f"grade {rubric.id!r}. Bias is not a property of the model alone — it lives in the "
            "rubric's wording too, and a clean run on one rubric says nothing about another."
        )

    return await grader.grade(explanation, rubric, attribution)


#: Appended to the judge's system prompt for a teach-back.
#:
#: **This is the belt, not the braces.** The guarantee is the probe above; a model asked nicely is a
#: model that will comply until the day it does not. It costs nothing and it helps, so it is here —
#: but nothing in PRAXIS *relies* on it.
TEACH_BACK_SYSTEM = """This response was SPOKEN and then transcribed.

You are grading WHAT THEY SAID. You are not grading how they said it.

- Non-native phrasing, unusual word order, missing articles: IGNORE. These tell you which \
languages this person speaks. They tell you nothing about whether they understand the concept.
- Hesitation, filler, false starts: mostly stripped before you saw this. IGNORE any that survived.
- Hedging -- "I think", "maybe", "I'm not sure": IGNORE COMPLETELY. Do not mark it down. A learner \
who is uncertain and says so is being honest with you, and penalising that teaches people to sound \
confident, which is the last thing anybody needs.
- Brevity: a short correct explanation is a correct explanation.

Grade the content against the rubric bands. Nothing else is on the rubric, so nothing else is on \
the mark."""
