"""**The grader cannot be downgraded.**

Not "should not". Cannot — without deleting one of these tests, which is a thing a human being has
to do on purpose, in a diff, in front of a reviewer.

The pressure on the routing table, for the whole life of this product, is downward. Grading is the
most expensive thing PRAXIS does and the least visible; nobody watches a grader. Sooner or later the
bill is up, someone notices the fast model costs a fifth as much and "seems fine on the ones we
spot-checked", and makes a change that looks like good engineering.

It would quietly gut C-4. The Evidence Ceiling says a model's opinion can never *confer* mastery —
but everything below the ceiling still rests on the grader being honest about what it actually saw,
and a cheaper grader is not a slightly worse grader. It is a grader that **abstains less** (M5.4),
and abstention is the one behaviour the entire design leans on.
"""

import pytest

from praxis_ai.config import Settings
from praxis_ai.models import JUDGEMENT_TASKS, Task, Tier, model_for, tier_for


@pytest.mark.parametrize("task", sorted(JUDGEMENT_TASKS, key=lambda t: t.value))
def test_anything_that_judges_a_learner_runs_on_the_heavy_model(task: Task) -> None:
    """A tutor turn that goes badly wastes ninety seconds and the learner says "no, I meant —".

    A grading call that goes badly writes a false belief into a model of a person's mind, and
    everything built on top of it inherits the error.
    """
    assert tier_for(task) is Tier.HEAVY, (
        f"{task.value} is a judgement about a learner and it has been routed to the cheap model. "
        "Whatever it now believes about them, it believes it more cheaply."
    )


def test_the_grader_is_never_the_fast_model() -> None:
    settings = Settings()

    assert model_for(Task.GRADE, settings) == settings.model_heavy
    assert model_for(Task.GRADE, settings) != settings.model_fast


def test_the_oracle_is_never_the_fast_model() -> None:
    """The oracle holds the answer key. It is the last thing that should be run on a budget."""
    settings = Settings()

    assert model_for(Task.CHECK_CLAIM, settings) == settings.model_heavy


def test_conversation_runs_on_the_fast_model_because_it_is_said_hundreds_of_times() -> None:
    """The other half of the argument. Routing everything to Opus is not caution — it is a product
    nobody can afford to run, which helps no learner at all."""
    assert tier_for(Task.TUTOR_TURN) is Tier.FAST
    assert tier_for(Task.GENERATE_ITEM) is Tier.FAST


def test_every_task_has_a_tier() -> None:
    """A new task must be given a tier *deliberately*. There is no default, because the default
    anyone would pick under deadline is the cheap one."""
    for task in Task:
        assert tier_for(task) in (Tier.HEAVY, Tier.FAST)


def test_the_tier_cannot_be_overridden_at_the_call_site() -> None:
    """``tier_for`` takes a task and nothing else. This is the whole mechanism.

    If a ``tier=`` or ``model=`` parameter ever appears on the client's ``complete()``, the table
    becomes advisory, and an advisory table is not a table — it is a comment.
    """
    import inspect

    from praxis_ai.claude import ClaudeClient

    parameters = set(inspect.signature(ClaudeClient.complete).parameters)

    assert "task" in parameters
    assert "model" not in parameters, "A call site can now choose its own model. The table is dead."
    assert "tier" not in parameters, "A call site can now choose its own tier. The table is dead."
