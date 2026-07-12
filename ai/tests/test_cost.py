"""**Nothing is spent anonymously, and nothing unknown is recorded as free.**"""

from decimal import Decimal

import pytest

from praxis_ai.config import Settings
from praxis_ai.cost import PRICES, Attribution, UnknownPriceError, assert_priced, price_of
from praxis_ai.models import Task, model_for

# ── Attribution ──────────────────────────────────────────────────────────────


def test_a_call_must_name_the_learner_it_is_spending_on() -> None:
    """There is no such thing as a call PRAXIS makes for its own benefit."""
    with pytest.raises(ValueError, match="attributed to a learner"):
        Attribution(learner_id="", concept_id="bm25")


def test_a_call_must_name_the_concept_it_is_about() -> None:
    with pytest.raises(ValueError, match="attributed to a concept"):
        Attribution(learner_id="alice", concept_id="   ")


# ── Pricing ──────────────────────────────────────────────────────────────────


def test_a_call_is_priced_from_the_tokens_it_actually_used() -> None:
    cost = price_of("claude-sonnet-5", input_tokens=1_000_000, output_tokens=1_000_000)

    assert cost.usd == Decimal("18.00")   # 3 in + 15 out
    assert cost.is_priced


def test_a_small_call_is_priced_exactly_and_not_rounded_to_nothing() -> None:
    """Decimal, not float. A tenth of a cent, a million times, is a real number."""
    cost = price_of("claude-opus-4-8", input_tokens=1_000, output_tokens=500)

    # (1000 * 15 + 500 * 75) / 1e6
    assert cost.usd == Decimal("0.0525")


def test_an_unpriced_model_costs_unknown_and_never_zero() -> None:
    """The shortcut is ``PRICES.get(model, 0.0)``, and it is how budgets die.

    A model gets swapped in, every call it makes is recorded as free, the dashboard stays flat, and
    the first anyone hears of it is the invoice.
    """
    cost = price_of("claude-something-nobody-priced", input_tokens=10_000, output_tokens=10_000)

    assert cost.usd is None
    assert cost.is_priced is False

    # And it is emphatically not zero.
    assert cost.usd != Decimal(0)

    # The Ledger is told, in as many words, that this number is not known.
    assert cost.as_log_fields()["priced"] is False
    assert cost.as_log_fields()["usd"] is None


# ── The startup gate ─────────────────────────────────────────────────────────


def test_every_model_praxis_routes_to_has_a_price() -> None:
    """The one that catches the real mistake: someone changes MODEL_FAST in .env to a model that
    has never been priced, and every tutor turn is thereafter billed at an unknown rate."""
    settings = Settings()

    for task in Task:
        model = model_for(task, settings)

        assert model in PRICES, (
            f"{task.value} routes to {model}, which has no price. Every call it makes would be "
            "recorded against a learner as if it were free."
        )


def test_the_app_refuses_to_start_on_an_unpriced_model() -> None:
    """Failing at deploy time is a nuisance for us. Failing later is a nuisance for a learner."""
    with pytest.raises(UnknownPriceError, match="No price for"):
        assert_priced({"claude-opus-4-8", "claude-imaginary-9"})


def test_a_fully_priced_configuration_starts() -> None:
    assert_priced({"claude-opus-4-8", "claude-sonnet-5"})
