"""A rubric: what "good" means for one concept, written down **before** anyone answers.

Open responses — explanations, teach-backs, critiques — are the majority of evidence by weight. If
PRAXIS grades them badly the whole Ledger is worthless, so the standard is written first, in bands,
by a human, and the model's only job is to say which band it sees.

## The rubric is answer-adjacent, and is treated as such

A band description like *"names resting limit orders absorbing arriving flow AND explains
depletion"* is, for practical purposes, **the answer**. So a rubric goes to a **grader** and never
to a **tutor** — the same rule as the oracle's key (M5.3), for the same reason — and it is never
logged.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

#: Bands are 0-4. 4 = expert, 3 = competent, 2 = descriptive, 1 = restatement, 0 = absent/wrong.
MAX_BAND = 4


@dataclass(frozen=True, slots=True)
class Dimension:
    id: str
    weight: float

    #: What the judge is being asked to look for.
    probe: str

    #: band → what that band looks like. The judge picks one; it does not invent a number.
    bands: dict[int, str]

    #: A band at or below this flags a known misconception, by key. Not free text.
    auto_flag: tuple[int, str] | None = None


@dataclass(frozen=True, slots=True)
class HardFail:
    """A deterministic pre-check, run **before any model is consulted**.

    *Where truth is deterministic, we use determinism.* A response that says a support level
    "can't fall below" is wrong in a way that does not require an opinion, and asking a language
    model to weigh it in is how half the AI-grading disasters in this industry happen.
    """

    pattern: str
    misconception: str

    #: The score this response is capped at, no matter how eloquent the rest of it was.
    cap: float

    def matches(self, response: str) -> bool:
        return re.search(self.pattern, response, re.IGNORECASE) is not None


@dataclass(frozen=True, slots=True)
class Rubric:
    id: str
    concept_id: str
    audience: str

    dimensions: tuple[Dimension, ...]

    #: The contract threshold. Below it the learner did not demonstrate the concept.
    pass_score: float = 0.75

    hard_fails: tuple[HardFail, ...] = field(default_factory=tuple)

    #: A response shorter than this is not an explanation, and no judge is spent on it.
    min_length: int = 40

    def __post_init__(self) -> None:
        if not self.dimensions:
            raise ValueError("A rubric with no dimensions grades nothing.")

        total = sum(d.weight for d in self.dimensions)

        if abs(total - 1.0) > 1e-6:
            raise ValueError(
                f"Rubric {self.id!r} dimension weights sum to {total:.3f}, not 1.0. A rubric whose "
                "weights do not sum to one produces a score that means nothing, and it would still "
                "look like a number."
            )

    def score_of(self, bands: dict[str, int]) -> float:
        """Weighted mean of the bands, on 0-1. The only arithmetic in grading."""
        return sum(d.weight * bands[d.id] for d in self.dimensions) / MAX_BAND

    def misconceptions_in(self, bands: dict[str, int]) -> list[str]:
        flagged = []

        for dimension in self.dimensions:
            if dimension.auto_flag is None:
                continue

            at_or_below, key = dimension.auto_flag

            if bands[dimension.id] <= at_or_below:
                flagged.append(key)

        return flagged
