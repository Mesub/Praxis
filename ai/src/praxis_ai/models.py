"""The model router.

Which model answers which question, decided by a **table** rather than by whoever is editing the
call site that afternoon.

## Why this is a table and not a parameter

The pressure on this file, for the entire life of the product, will be *downward*. Grading is the
most expensive thing PRAXIS does and the least visible: nobody watches a grader. So there will come
a quarter where the bill is up and somebody notices that grading on the fast model is a quarter of
the price and "seems about as good in the cases we spot-checked".

That change would not look like a betrayal. It would look like good engineering.

And it would quietly gut C-4. The **Evidence Ceiling** says an LLM's opinion can never confer
mastery — but everything *below* the ceiling still rests on the grader being honest about what it
saw, and a cheaper grader is not a slightly worse grader. It is a grader that abstains less (M5.4),
which is the one behaviour the whole design depends on.

So the tier is a property of the **task**, not of the call site. Downgrading the grader requires
editing this table, with this comment in the diff, in front of a reviewer.
"""

from __future__ import annotations

from enum import StrEnum

from praxis_ai.config import Settings


class Task(StrEnum):
    """Everything PRAXIS asks a model to do. There is no 'other'."""

    # ── Judgement. These decide what PRAXIS believes about a person. ──────────
    GRADE = "grade"
    """Marking a learner's work. The output becomes Evidence, and Evidence moves mastery."""

    CHECK_CLAIM = "check_claim"
    """The oracle (M5.3). Holds the answer; the tutor never sees it."""

    SYNTHESISE = "synthesise"
    """Reading a learner's whole history and saying something true about it."""

    COMPILE = "compile"
    """Turning a stated goal into an objective on the graph."""

    # ── Conversation and construction. Frequent, cheaper, and reversible. ─────
    TUTOR_TURN = "tutor_turn"
    """One turn of Socratic dialogue. Said hundreds of times a session; wrong is recoverable."""

    GENERATE_ITEM = "generate_item"
    """A never-seen question. Checked by the golden set (M5.6) before it reaches anyone."""


class Tier(StrEnum):
    HEAVY = "heavy"
    """Opus-class. Judgement, and anything whose output PRAXIS will act on as if it were true."""

    FAST = "fast"
    """Sonnet-class. Talking, and drafting."""


#: **The table.** Every task, named, with the tier it is allowed to use.
#:
#: Note what is in HEAVY: everything that *decides something about a learner*. A tutor turn that
#: goes badly wastes ninety seconds and the learner says "no, I meant —". A grading call that goes
#: badly writes a false belief into a model of a person's mind, and it decays from there.
_ROUTING: dict[Task, Tier] = {
    Task.GRADE: Tier.HEAVY,
    Task.CHECK_CLAIM: Tier.HEAVY,
    Task.SYNTHESISE: Tier.HEAVY,
    Task.COMPILE: Tier.HEAVY,
    Task.TUTOR_TURN: Tier.FAST,
    Task.GENERATE_ITEM: Tier.FAST,
}

#: Tasks whose output PRAXIS treats as a **judgement about a learner**. These may never be routed to
#: a cheaper tier — not as a matter of policy, but as a matter of arithmetic in a test.
JUDGEMENT_TASKS = frozenset({Task.GRADE, Task.CHECK_CLAIM, Task.SYNTHESISE, Task.COMPILE})


def tier_for(task: Task) -> Tier:
    """Pure. Given a task, the tier — with no way to pass an override."""
    try:
        return _ROUTING[task]
    except KeyError as missing:  # pragma: no cover — unreachable while Task is an enum
        raise ValueError(
            f"{task!r} has no routing entry. A new task must be given a tier deliberately: "
            "defaulting it to the cheap model is how a grader gets downgraded by accident."
        ) from missing


def model_for(task: Task, settings: Settings) -> str:
    """The concrete model id this task runs on."""
    return settings.model_heavy if tier_for(task) is Tier.HEAVY else settings.model_fast
