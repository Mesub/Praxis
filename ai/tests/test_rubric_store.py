"""The rubric store: it holds the standard, it refuses when it has none, and the seed is real."""

from __future__ import annotations

import pytest

from praxis_ai.routes.assessment import get_rubric_store
from praxis_ai.rubric import Rubric
from praxis_ai.rubric_store import (
    InMemoryRubricStore,
    UnknownRubricError,
    default_rubric_store,
    require,
    seed_rubrics,
)


def test_add_and_get_round_trip() -> None:
    store = InMemoryRubricStore()
    (rubric,) = seed_rubrics()
    store.add(rubric)
    assert store.get(rubric.concept_id) is rubric


def test_unknown_concept_returns_none_not_a_guess() -> None:
    assert InMemoryRubricStore().get("no-such-concept") is None


def test_require_refuses_rather_than_inventing_a_standard() -> None:
    with pytest.raises(UnknownRubricError):
        require(InMemoryRubricStore(), "no-such-concept")


def test_default_store_is_seeded_and_resolvable_by_concept() -> None:
    store = default_rubric_store()
    rubric = store.get("inverted-index")
    assert rubric is not None
    assert rubric.concept_id == "inverted-index"


def test_the_wired_dependency_serves_the_seeded_store() -> None:
    assert get_rubric_store().get("inverted-index") is not None


class TestTheSeededRubricIsUsable:
    """A seed that only *exists* is not enough — it has to grade."""

    @staticmethod
    def _rubric() -> Rubric:
        rubric = default_rubric_store().get("inverted-index")
        assert rubric is not None
        return rubric

    def test_weights_sum_to_one(self) -> None:
        # The domain __post_init__ enforces this; constructing the seed at all proves it, and this
        # states the guarantee out loud.
        assert abs(sum(d.weight for d in self._rubric().dimensions) - 1.0) < 1e-6

    def test_a_top_answer_scores_above_pass(self) -> None:
        rubric = self._rubric()
        top = {d.id: 4 for d in rubric.dimensions}
        assert rubric.score_of(top) >= rubric.pass_score

    def test_a_restatement_flags_the_misconception(self) -> None:
        rubric = self._rubric()
        # Band 1 on the mechanism dimension is a restatement; it auto-flags the known confusion.
        bands = {"mechanism": 1, "tradeoff": 3}
        assert "confuses-index-with-store" in rubric.misconceptions_in(bands)

    def test_the_deterministic_hard_fail_catches_the_store_confusion(self) -> None:
        rubric = self._rubric()
        (hard_fail,) = rubric.hard_fails
        assert hard_fail.matches("an inverted index stores the documents in a big table")
        assert not hard_fail.matches("it maps each term to the documents that contain it")
