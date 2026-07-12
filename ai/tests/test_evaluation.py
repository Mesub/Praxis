"""**The one guarantee in this codebase that a type cannot give you.**

Everything else in the AI plane is structural: the answer is not in the tutor's context, the verdict
has no field for a sentence, the item is hashed for novelty. Those are true whether or not the model
is any good.

Grading quality is not like that. *"This grader agrees with expert humans"* is an **empirical
claim** about the world, and the only way to know it is still true is to keep checking. When it
stops being true, you either find out here — or you don't, and PRAXIS spends a year writing
confident falsehoods into models of people's minds.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from praxis_ai.evaluation import (
    AI_HUMAN_MIN,
    Calibration,
    GoldenSet,
    GraderRegressedError,
    UncertifiedGoldenSetError,
    calibrate,
    certified_sets,
    decision_agreement,
    load_golden_set,
    quadratic_weighted_kappa,
    systematic_bias,
)

GOLDEN_DIR = Path(__file__).resolve().parents[1] / "golden"

DIMENSIONS = ["mechanism", "invalidation"]


def _score(bands: dict[str, int]) -> float:
    return sum(bands[d] for d in DIMENSIONS) / (2 * 4)


# ── The metric ───────────────────────────────────────────────────────────────


def test_perfect_agreement_is_one() -> None:
    assert quadratic_weighted_kappa([0, 1, 2, 3, 4], [0, 1, 2, 3, 4]) == pytest.approx(1.0)


def test_disagreement_by_one_band_is_penalised_far_less_than_by_four() -> None:
    """**The whole reason this is QWK and not Cohen's kappa.**

    Plain Cohen's kappa scores these two graders *identically* — every disagreement is just "wrong".
    One of them is slightly generous. The other is catastrophically broken. A metric that cannot
    tell them apart is worse than no metric: it comes with a number that looks like knowledge.
    """
    truth = [0, 1, 2, 3, 4, 0, 1, 2, 3, 4]

    near_miss = [1, 2, 3, 4, 4, 1, 2, 3, 4, 4]   # always one band generous
    catastrophe = [4, 3, 2, 1, 0, 4, 3, 2, 1, 0]   # inverted

    assert quadratic_weighted_kappa(near_miss, truth) > quadratic_weighted_kappa(catastrophe, truth)

    # And the broken one is *worse than chance*, which is a real state worth being able to see.
    assert quadratic_weighted_kappa(catastrophe, truth) < 0.0


def test_a_grader_that_says_the_same_thing_every_time_agrees_with_nobody() -> None:
    """The degenerate grader: band 3 for everything. It is right a fair bit of the time, and it has
    learned nothing about anybody."""
    truth = [0, 1, 2, 3, 4, 0, 1, 2, 3, 4]
    lazy = [3] * 10

    assert quadratic_weighted_kappa(lazy, truth) <= 0.0


def test_two_raters_of_different_lengths_is_a_bug_and_says_so() -> None:
    with pytest.raises(ValueError, match="skipped something"):
        quadratic_weighted_kappa([1, 2, 3], [1, 2])


# ── The number the learner feels ─────────────────────────────────────────────


def test_decision_agreement_counts_only_the_flips() -> None:
    """A grader can have a beautiful QWK and still flip pass/fail on one response in ten, because
    QWK counts all the easy cases and the *decision* lives entirely at the boundary."""
    human = [0.90, 0.80, 0.74, 0.30]
    ai = [0.95, 0.78, 0.76, 0.35]   # the third one crosses the line the human did not

    assert decision_agreement(ai, human, threshold=0.75) == pytest.approx(0.75)


def test_a_uniformly_generous_grader_is_caught_by_bias_even_with_a_fine_kappa() -> None:
    """Every score inflated by 0.15. The *ordering* is perfect, so QWK is happy. Everybody's mastery
    is quietly inflating."""
    human = [0.20, 0.40, 0.60, 0.80]
    ai = [0.35, 0.55, 0.75, 0.95]

    assert systematic_bias(ai, human) == pytest.approx(0.15)


# ── The gate ─────────────────────────────────────────────────────────────────


def _calibration(**overrides) -> Calibration:
    defaults = dict(
        rubric_id="rub.test.v1",
        n=100,
        human_ceiling=0.85,
        ai_human=0.80,
        decision=0.97,
        bias=0.03,
    )

    return Calibration(**{**defaults, **overrides})


def test_a_good_grader_ships() -> None:
    _calibration().assert_ships()


def test_a_grader_below_the_agreement_floor_does_not_ship() -> None:
    """**A grader that regresses does not ship, even if it is cheaper.**"""
    with pytest.raises(GraderRegressedError, match="AI-human agreement"):
        _calibration(ai_human=0.70).assert_ships()


def test_when_the_humans_cannot_agree_the_rubric_is_blamed_and_not_the_model() -> None:
    """**The most easily-got-wrong rule in the whole harness.**

    The instinct when the numbers are bad is to tune the judges. But if two experts cannot agree on
    what the bands *mean*, tuning a model against them produces a grader that reliably reproduces
    their confusion — and it will have an excellent QWK while doing it.
    """
    failures = _calibration(human_ceiling=0.55).failures()

    assert any("rubric is broken, not the model" in f for f in failures)
    assert any("do not touch the judges" in f.lower() for f in failures)


def test_a_grader_that_cannot_reach_the_human_ceiling_does_not_ship() -> None:
    with pytest.raises(GraderRegressedError, match="human ceiling"):
        _calibration(human_ceiling=0.95, ai_human=0.76).assert_ships()


def test_a_uniformly_generous_grader_does_not_ship() -> None:
    with pytest.raises(GraderRegressedError, match="mastery quietly inflates"):
        _calibration(bias=0.18).assert_ships()


def test_a_grader_that_flips_the_decision_does_not_ship() -> None:
    with pytest.raises(GraderRegressedError, match="only number the learner actually feels"):
        _calibration(decision=0.88).assert_ships()


def test_the_floor_is_the_one_canon_asked_for() -> None:
    assert AI_HUMAN_MIN == 0.75


# ── A golden set with one rater is not a golden set ──────────────────────────


def _golden(tmp_path: Path, *, double_graded: bool, raters: list[str]) -> GoldenSet:
    payload = {
        "rubric_id": "rub.test.v1",
        "concept_id": "c",
        "double_graded": double_graded,
        "responses": [
            {
                "id": f"r-{i}",
                "response": "...",
                "raters": {name: {"mechanism": i % 5, "invalidation": i % 5} for name in raters},
                "adjudicated": {"mechanism": i % 5, "invalidation": i % 5},
            }
            for i in range(10)
        ],
    }

    path = tmp_path / "set.json"
    path.write_text(json.dumps(payload), encoding="utf-8")

    return load_golden_set(path)


def test_a_set_marked_double_graded_by_nobody_certifies_nothing(tmp_path: Path) -> None:
    """It measures agreement with **one person's opinion**. If that person was having a bad
    afternoon, the grader is now calibrated to a bad afternoon — and the dashboard says 0.83 either
    way."""
    golden = _golden(tmp_path, double_graded=False, raters=["alice"])

    with pytest.raises(UncertifiedGoldenSetError, match="bad afternoon"):
        golden.certify()


def test_one_rater_certifies_nothing_even_if_someone_ticked_the_box(tmp_path: Path) -> None:
    """The flag is a claim, and the data has to back it."""
    golden = _golden(tmp_path, double_graded=True, raters=["alice"])

    with pytest.raises(UncertifiedGoldenSetError, match="decimal point"):
        golden.certify()


def test_an_empty_golden_set_agrees_with_everything(tmp_path: Path) -> None:
    path = tmp_path / "empty.json"
    path.write_text(
        json.dumps(
            {"rubric_id": "r", "concept_id": "c", "double_graded": True, "responses": []}
        ),
        encoding="utf-8",
    )

    with pytest.raises(UncertifiedGoldenSetError, match="agrees with everything"):
        load_golden_set(path).certify()


def test_double_graded_is_never_defaulted_to_true(tmp_path: Path) -> None:
    """A missing flag means nobody said it was double-graded, which means it was not."""
    path = tmp_path / "silent.json"
    path.write_text(
        json.dumps({"rubric_id": "r", "concept_id": "c", "responses": []}), encoding="utf-8"
    )

    assert load_golden_set(path).double_graded is False


def test_a_properly_double_graded_set_certifies(tmp_path: Path) -> None:
    golden = _golden(tmp_path, double_graded=True, raters=["alice", "bob"])

    golden.certify()   # does not raise


# ── End to end, on a set that is entitled to certify ─────────────────────────


def test_a_regressed_grader_is_caught_by_the_harness_end_to_end(tmp_path: Path) -> None:
    golden = _golden(tmp_path, double_graded=True, raters=["alice", "bob"])

    # A grader that inverts the scale. Every response, exactly wrong.
    inverted = {r.id: {d: 4 - r.adjudicated[d] for d in DIMENSIONS} for r in golden.responses}

    report = calibrate(golden, inverted, DIMENSIONS, _score, threshold=0.75)

    assert report.ai_human < 0
    assert report.failures()

    with pytest.raises(GraderRegressedError):
        report.assert_ships()


def test_a_perfect_grader_passes_the_harness_end_to_end(tmp_path: Path) -> None:
    golden = _golden(tmp_path, double_graded=True, raters=["alice", "bob"])

    perfect = {r.id: dict(r.adjudicated) for r in golden.responses}

    report = calibrate(golden, perfect, DIMENSIONS, _score, threshold=0.75)

    report.assert_ships()

    assert report.ai_human == pytest.approx(1.0)
    assert report.decision == pytest.approx(1.0)


def test_a_harness_that_graded_nothing_does_not_report_success(tmp_path: Path) -> None:
    """The silent catastrophe: the grader errored on every response, the harness measured an empty
    list, and the dashboard went green."""
    golden = _golden(tmp_path, double_graded=True, raters=["alice", "bob"])

    with pytest.raises(UncertifiedGoldenSetError, match="measures nothing"):
        calibrate(golden, {}, DIMENSIONS, _score, threshold=0.75)


# ── THE GATE, on the real repository ─────────────────────────────────────────


def test_praxis_makes_no_calibration_claim_it_has_not_earned() -> None:
    """**The honest state of this repository, asserted rather than glossed over.**

    `ai/golden/` contains one seed set, authored from the worked examples in docs/12. It has *one*
    rater — me — so it certifies nothing, and the harness says so.

    This test therefore asserts something uncomfortable: **PRAXIS currently has no calibrated
    grader.** Every structural guarantee in the AI plane holds (the tutor has no answer, the ceiling
    is arithmetic, the item is novel), but the empirical claim "this grader marks like an expert" is
    *unproven*, and will stay unproven until two humans sit down and double-grade a real set.

    When that happens, this test flips to the loop below and the gate goes live. Until then the
    honest thing is to say out loud that the number does not exist — rather than shipping a golden
    set I marked myself and calling the result calibration.
    """
    certified = certified_sets(GOLDEN_DIR)

    assert certified == [], (
        "A golden set is now certified. Good — delete this assertion and let the gate below run."
    )


@pytest.mark.parametrize("golden", certified_sets(GOLDEN_DIR), ids=lambda g: g.rubric_id)
def test_the_grader_still_agrees_with_the_humans(golden: GoldenSet) -> None:
    """**The gate.** Empty today, armed for the day it is not.

    It runs against *recorded* grader output rather than a live model, deliberately: a calibration
    gate that only runs when someone has an API key is a gate that does not run.
    """
    raise AssertionError(   # pragma: no cover — no certified sets yet
        f"{golden.rubric_id} is certified but no recorded grader output exists for it. Run the "
        "grader over the golden set, commit its bands, and wire them in here."
    )
