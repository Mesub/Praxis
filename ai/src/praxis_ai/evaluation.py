"""**The golden set: the only thing standing between PRAXIS and a grader nobody is checking.**

Everything else in the AI plane is a structural guarantee — the answer is not in the tutor's
context,
the verdict has no field for a sentence, the item is hashed for novelty. Those hold whether or not
the model is any good.

This does not. Grading quality is an *empirical* claim: **this grader agrees with expert humans**.
There is no type you can write that makes it true. You measure it, against work that humans have
already marked, and when it stops being true you find out — or you don't, and PRAXIS spends a year
writing confident falsehoods into models of people's minds.

## Cohen's κ is the wrong metric, and the roadmap says Cohen's κ

Plain Cohen's kappa treats **every disagreement as equally wrong**. A judge that says band 4 where
the human said 3 is scored exactly as badly as one that says band 4 where the human said 0.

On an *ordered* scale that is nonsense, and it is dangerous nonsense: it hides the difference
between
a grader that is slightly generous and a grader that is wildly broken, which is the single most
important distinction this harness exists to draw.

So the metric is **quadratic weighted kappa** (QWK), which penalises a disagreement by the *square*
of its distance — near-misses barely count, and a 4-vs-0 is catastrophic. This is what CANON §5.4
actually specifies, and the roadmap's "Cohen's κ" is loose wording. Amended there, not fudged here.

## And a golden set with one rater is not a golden set

It measures agreement with **one person's opinion**. If that person was having a bad afternoon, the
grader is now calibrated to a bad afternoon, and the number on the dashboard is 0.83 either way.

CANON demands two independent expert raters plus an adjudicator. Until a rubric's golden set has
them, this harness **refuses to certify it** — and PRAXIS makes no claim about that rubric's grading
quality. A refusal to certify is not a bug in the harness. It is the harness working.
"""

from __future__ import annotations

import json
import logging
from collections.abc import Sequence
from dataclasses import dataclass
from pathlib import Path

from praxis_ai.rubric import MAX_BAND

log = logging.getLogger("praxis.ai.evaluation")

# ── The gates, from CANON §5.4 ───────────────────────────────────────────────

#: If the *humans* cannot agree this well, **the rubric is broken, not the model.** Tuning the judge
#: pool against an incoherent rubric produces a grader that reliably reproduces confusion.
HUMAN_CEILING_MIN = 0.80

#: AI-human agreement. Below this, the grader does not ship.
AI_HUMAN_MIN = 0.75

#: And it must also reach 90% of what the humans themselves managed. A grader cannot be more
#: reliable than the standard it was measured against, and a suspiciously high number against a
#: shaky ceiling means the test is broken, not that the model is superhuman.
CEILING_FRACTION_MIN = 0.90

#: |mean(AI) - mean(human)|. A grader that is uniformly 0.15 generous has a QWK that looks fine and
#: is quietly inflating everybody's mastery.
MAX_SYSTEMATIC_BIAS = 0.10

#: Pass/fail at the contract threshold — **the only number the learner actually feels.** A grader
#: can
#: have a beautiful QWK and still flip the decision on one response in ten.
DECISION_AGREEMENT_MIN = 0.95

#: Two raters and an adjudicator. Fewer is one person's opinion with a decimal point on it.
MIN_RATERS = 2


class UncertifiedGoldenSetError(RuntimeError):
    """This golden set cannot certify anything, and PRAXIS will not pretend otherwise."""


class GraderRegressedError(AssertionError):
    """**A grader that regresses does not ship, even if it is cheaper.**

    Raised by the pytest gate. It is an ``AssertionError`` on purpose: it fails the build.
    """


# ── Metrics ──────────────────────────────────────────────────────────────────


def quadratic_weighted_kappa(
    a: Sequence[int], b: Sequence[int], max_band: int = MAX_BAND
) -> float:
    """Agreement on an **ordered** scale, penalising disagreement by its squared distance.

    1.0 is perfect agreement. 0.0 is what you would get by chance. **Negative is worse than
    chance**,
    which is a real state and worth being able to see.
    """
    if len(a) != len(b):
        raise ValueError("Two raters, different numbers of ratings. One of them skipped something.")

    if not a:
        raise ValueError("No ratings. There is nothing to agree about.")

    size = max_band + 1
    n = len(a)

    observed = [[0.0] * size for _ in range(size)]

    for x, y in zip(a, b, strict=True):
        observed[x][y] += 1.0

    rows = [sum(observed[i]) for i in range(size)]
    cols = [sum(observed[i][j] for i in range(size)) for j in range(size)]

    numerator = 0.0
    denominator = 0.0

    for i in range(size):
        for j in range(size):
            weight = ((i - j) ** 2) / ((size - 1) ** 2)

            expected = rows[i] * cols[j] / n

            numerator += weight * observed[i][j]
            denominator += weight * expected

    if denominator == 0:
        # Every rating identical on both sides. Perfect agreement — and chance would also have
        # produced it, which is why kappa is undefined here rather than 1.0. We say 1.0 and mean
        # "there is nothing here to disagree about".
        return 1.0

    return 1.0 - (numerator / denominator)


def decision_agreement(
    ai_scores: Sequence[float], human_scores: Sequence[float], threshold: float
) -> float:
    """**The only number the learner actually feels.**

    They never see a band or a kappa. They see *passed* or *did not pass*, and a grader with a
    beautiful QWK can still flip that decision on one response in ten — because QWK counts all the
    easy cases, and the decision lives entirely at the boundary.
    """
    agreed = sum(
        (ai >= threshold) == (human >= threshold)
        for ai, human in zip(ai_scores, human_scores, strict=True)
    )

    return agreed / len(ai_scores)


def systematic_bias(ai_scores: Sequence[float], human_scores: Sequence[float]) -> float:
    """A grader uniformly 0.15 generous has a fine QWK and is inflating everybody's mastery."""
    ai_mean = sum(ai_scores) / len(ai_scores)
    human_mean = sum(human_scores) / len(human_scores)

    return abs(ai_mean - human_mean)


# ── The golden set ───────────────────────────────────────────────────────────


@dataclass(frozen=True, slots=True)
class GoldenResponse:
    """One learner response, and what the humans said about it."""

    id: str
    response: str

    #: rater name → their bands, per dimension. **Two or more, or this set certifies nothing.**
    raters: dict[str, dict[str, int]]

    #: The adjudicated truth, where the raters disagreed. This is what the AI is measured against.
    adjudicated: dict[str, int]


@dataclass(frozen=True, slots=True)
class GoldenSet:
    rubric_id: str
    concept_id: str
    responses: tuple[GoldenResponse, ...]

    #: Set by a human who actually ran the double-grading. Never inferred, never defaulted to True.
    double_graded: bool

    @property
    def rater_names(self) -> set[str]:
        return {name for r in self.responses for name in r.raters}

    def certify(self) -> None:
        """Raise unless this set is entitled to certify a grader.

        Every branch below is a way of quietly measuring nothing and printing a number anyway.
        """
        if not self.responses:
            raise UncertifiedGoldenSetError(
                f"{self.rubric_id}: an empty golden set agrees with everything."
            )

        if not self.double_graded:
            raise UncertifiedGoldenSetError(
                f"{self.rubric_id}: this set is not double-graded. It has not been marked "
                "independently by two experts, so it measures agreement with one person's "
                "opinion — and if that person was having a bad afternoon, the grader is now "
                "calibrated to a bad afternoon and the dashboard says 0.83 either way. PRAXIS "
                "makes no claim about this rubric's grading quality."
            )

        if len(self.rater_names) < MIN_RATERS:
            raise UncertifiedGoldenSetError(
                f"{self.rubric_id}: {len(self.rater_names)} rater(s). CANON §5.4 requires "
                f"{MIN_RATERS} independent experts plus an adjudicator. Fewer is one person's "
                "opinion with a decimal point on it."
            )


def load_golden_set(path: Path) -> GoldenSet:
    raw = json.loads(path.read_text(encoding="utf-8"))

    return GoldenSet(
        rubric_id=raw["rubric_id"],
        concept_id=raw["concept_id"],
        # **Never defaulted to True.** A missing flag means nobody said it was double-graded, which
        # means it was not.
        double_graded=bool(raw.get("double_graded", False)),
        responses=tuple(
            GoldenResponse(
                id=r["id"],
                response=r["response"],
                raters={name: bands for name, bands in r["raters"].items()},
                adjudicated=r["adjudicated"],
            )
            for r in raw["responses"]
        ),
    )


# ── The report, and the gate ─────────────────────────────────────────────────


@dataclass(frozen=True, slots=True)
class Calibration:
    rubric_id: str
    n: int

    human_ceiling: float
    ai_human: float
    decision: float
    bias: float

    @property
    def ceiling_fraction(self) -> float:
        return 1.0 if self.human_ceiling <= 0 else self.ai_human / self.human_ceiling

    def failures(self) -> list[str]:
        """Every reason this grader must not ship. Empty means it may."""
        problems = []

        # **Checked first, and it blames the rubric.** If two experts cannot agree on what the bands
        # mean, tuning a model against them produces a grader that reliably reproduces confusion.
        if self.human_ceiling < HUMAN_CEILING_MIN:
            problems.append(
                f"The HUMANS only agreed at QWK {self.human_ceiling:.3f} (< {HUMAN_CEILING_MIN}). "
                "The rubric is broken, not the model. Rewrite the bands — do not touch the judges."
            )

        if self.ai_human < AI_HUMAN_MIN:
            problems.append(
                f"AI-human agreement is QWK {self.ai_human:.3f} (< {AI_HUMAN_MIN})."
            )

        if self.ceiling_fraction < CEILING_FRACTION_MIN:
            problems.append(
                f"The grader reached {self.ceiling_fraction:.0%} of the human ceiling "
                f"(< {CEILING_FRACTION_MIN:.0%})."
            )

        if self.bias > MAX_SYSTEMATIC_BIAS:
            problems.append(
                f"Systematic bias {self.bias:.3f} (> {MAX_SYSTEMATIC_BIAS}). The grader is "
                "uniformly generous or uniformly harsh, which a good QWK will happily hide while "
                "everybody's mastery quietly inflates."
            )

        if self.decision < DECISION_AGREEMENT_MIN:
            problems.append(
                f"Pass/fail agreement is {self.decision:.1%} (< {DECISION_AGREEMENT_MIN:.0%}). "
                "This is the only number the learner actually feels."
            )

        return problems

    def assert_ships(self) -> None:
        """**A grader that regresses does not ship, even if it is cheaper.**"""
        if problems := self.failures():
            raise GraderRegressedError(
                f"The grader for {self.rubric_id} may not ship:\n  - "
                + "\n  - ".join(problems)
                + "\n\nThis is the gate. It is not advisory, and it is not a flake. A grader that "
                "regresses writes false beliefs into models of people's minds, and they will meet "
                "those beliefs months later as work that is inexplicably too hard."
            )


def calibrate(
    golden: GoldenSet,
    ai_bands: dict[str, dict[str, int]],
    dimension_ids: Sequence[str],
    score_of,
    threshold: float,
) -> Calibration:
    """Measure a grader against humans who already did the work.

    ``ai_bands`` maps response id → the bands the grader produced. Passing it in (rather than
    calling
    the grader here) is what lets the *gate* be tested without an API key, and the *grader* be
    tested
    without the gate.
    """
    golden.certify()

    raters = sorted(golden.rater_names)

    scored = [r for r in golden.responses if r.id in ai_bands]

    if not scored:
        raise UncertifiedGoldenSetError(
            f"{golden.rubric_id}: the grader produced no bands for any golden response. A harness "
            "that measures nothing and reports 1.0 is worse than no harness at all."
        )

    # Flattened across dimensions: agreement is per-band, not per-response.
    def flat(source) -> list[int]:
        return [source(r)[d] for r in scored for d in dimension_ids]

    human_a = flat(lambda r: r.raters[raters[0]])
    human_b = flat(lambda r: r.raters[raters[1]])

    truth = flat(lambda r: r.adjudicated)
    ai = flat(lambda r: ai_bands[r.id])

    ai_scores = [score_of(ai_bands[r.id]) for r in scored]
    human_scores = [score_of(r.adjudicated) for r in scored]

    return Calibration(
        rubric_id=golden.rubric_id,
        n=len(scored),
        human_ceiling=quadratic_weighted_kappa(human_a, human_b),
        ai_human=quadratic_weighted_kappa(ai, truth),
        decision=decision_agreement(ai_scores, human_scores, threshold),
        bias=systematic_bias(ai_scores, human_scores),
    )


def certified_sets(directory: Path) -> list[GoldenSet]:
    """Every golden set in ``directory`` that is entitled to certify a grader.

    Usually empty, at the moment, and that is the honest answer rather than an oversight.
    """
    found = []

    for path in sorted(directory.glob("*.json")):
        golden = load_golden_set(path)

        try:
            golden.certify()
        except UncertifiedGoldenSetError as why:
            log.warning("Golden set not certified: %s", why)
            continue

        found.append(golden)

    return found
