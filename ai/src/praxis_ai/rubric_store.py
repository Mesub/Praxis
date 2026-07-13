"""The rubric store — answer-adjacent, and therefore on the far side of the wire.

A rubric's band descriptions are, in practice, the answer (see :mod:`praxis_ai.rubric`). So rubrics
live **here**, on the AI plane, for the same reason the oracle's answer keys do: to keep the .NET
core — the process that serves the tutor — with no answer in it to leak (C-5). The core asks the
grader to grade *a concept*; the grader looks the rubric up here and grades against it. The rubric
never crosses back over the wire, is never logged, and is never placed in the tutor's context.

This is the storage half (M11.7a). The grade endpoint that resolves a rubric by concept and grades
against it is the next slice; keeping them apart keeps each one small and its test honest.
"""

from __future__ import annotations

from typing import Protocol

from praxis_ai.rubric import Dimension, HardFail, Rubric


class UnknownRubricError(KeyError):
    """No rubric for this concept.

    PRAXIS refuses to grade rather than inventing a standard on the spot — a grader that makes up
    what "good" means is worse than one that declines, because its number still looks like a mark.
    """


class RubricStore(Protocol):
    """Read side of the store. Grading needs only *get*; authoring is a separate concern."""

    def get(self, concept_id: str) -> Rubric | None: ...


class InMemoryRubricStore:
    """The Phase-0 store. In-memory, seeded at construction, mirroring ``InMemoryAnswerKeyStore``.

    A durable, per-concept-authored rubric library arrives with the concept pack (M13); until
    then the shape and the guarantee are what matter, and both are exercised here.
    """

    def __init__(self, rubrics: dict[str, Rubric] | None = None) -> None:
        self._rubrics: dict[str, Rubric] = dict(rubrics or {})

    def add(self, rubric: Rubric) -> None:
        """One rubric per concept. A second would be a second definition of "good"."""
        self._rubrics[rubric.concept_id] = rubric

    def get(self, concept_id: str) -> Rubric | None:
        return self._rubrics.get(concept_id)


def require(store: RubricStore, concept_id: str) -> Rubric:
    """The rubric for a concept, or a refusal — so the grade path never guesses at a missing one."""
    rubric = store.get(concept_id)
    if rubric is None:
        raise UnknownRubricError(
            f"No rubric is authored for concept {concept_id!r}. PRAXIS will not grade an "
            "explanation against a standard it does not have."
        )
    return rubric


# ── The seed ──────────────────────────────────────────────────────────────────
#
# One real EXPLAIN rubric, for the inverted index — the concept the teach-back doc reasons about
# (praxis_ai.rubric, Domain/TeachBack.cs). Two dimensions, weights summing to 1.0 (the domain
# refuses any other sum), bands 0-4, and a deterministic hard-fail for the classic "it stores the
# documents" confusion so that error does not need a model's opinion.

_INVERTED_INDEX = Rubric(
    id="rub.inverted-index.explain.v1",
    concept_id="inverted-index",
    audience="a smart friend who has never written a search engine",
    pass_score=0.75,
    dimensions=(
        Dimension(
            id="mechanism",
            weight=0.6,
            probe="Do they explain the term-to-document mapping, not just name it?",
            bands={
                4: "Term to posting list, and why that beats scanning every document.",
                3: "Term to the documents that contain it, correctly.",
                2: "Descriptive only — says it 'indexes' without the mapping.",
                1: "Restates the name.",
                0: "Absent or wrong.",
            },
            auto_flag=(1, "confuses-index-with-store"),
        ),
        Dimension(
            id="tradeoff",
            weight=0.4,
            probe="Do they say what it costs — build/update time, or space?",
            bands={
                4: "Names the write-time or space cost and when it is worth paying.",
                3: "Mentions a cost without detail.",
                2: "Hints that nothing is free but names nothing.",
                1: "Claims it is free.",
                0: "Absent.",
            },
        ),
    ),
    hard_fails=(
        HardFail(
            pattern=r"\bstores?\s+the\s+documents?\b",
            misconception="confuses-index-with-store",
            cap=0.4,
        ),
    ),
)


def seed_rubrics() -> tuple[Rubric, ...]:
    """The rubrics PRAXIS ships with in Phase 0. One, authored by hand, exercised end to end."""
    return (_INVERTED_INDEX,)


def default_rubric_store() -> InMemoryRubricStore:
    """A store seeded with the Phase-0 rubrics."""
    store = InMemoryRubricStore()
    for rubric in seed_rubrics():
        store.add(rubric)
    return store
