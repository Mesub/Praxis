"""What a call cost, and **who it was spent on**.

## An unattributed call is a bug, not a convenience

Every model call PRAXIS makes is made *on behalf of one learner, about one concept*. There is no
such thing as a background call, a warm-up call, or a call for the system's own benefit — and the
moment there is, PRAXIS can no longer answer the only question that keeps it honest about its own
economics: **what does this person cost, and is it less than they pay?**

So attribution is a required argument. Not a default, not an optional field, not something a
middleware fills in if you forgot. You cannot spend a learner's money without saying whose it is.

## An unpriced model costs *unknown*, never *zero*

The tempting shortcut is `PRICES.get(model, 0.0)`. It is how budgets die: a model gets swapped in,
every call it makes is recorded as free, the dashboard stays flat, and the first anyone hears of it
is the invoice.

So an unpriced model yields a cost of `None` — an honest "we do not know" that the Ledger (M10) can
see and flag — and the app **refuses to start** if a model it is routing to has no price. Failing at
deploy time is a nuisance for us. Failing in front of a learner is a nuisance for them.
"""

from __future__ import annotations

from dataclasses import dataclass
from decimal import Decimal

#: USD per **million** tokens, from Anthropic's published pricing.
#:
#: This is a *claim about the world*, and the world changes it without telling us. It lives here, in
#: one table, so that when it is wrong it is wrong in exactly one place — and so that adding a model
#: without pricing it is a startup failure rather than a silent zero.
PRICES: dict[str, tuple[Decimal, Decimal]] = {
    # model id: (input, output)
    "claude-opus-4-8": (Decimal("15.00"), Decimal("75.00")),
    "claude-opus-4-7": (Decimal("15.00"), Decimal("75.00")),
    "claude-sonnet-5": (Decimal("3.00"), Decimal("15.00")),
    "claude-haiku-4-5-20251001": (Decimal("0.80"), Decimal("4.00")),
}

_PER_MILLION = Decimal(1_000_000)


class UnknownPriceError(KeyError):
    """Raised only at startup, where it is a deploy-time problem rather than a learner's problem."""


@dataclass(frozen=True, slots=True)
class Attribution:
    """Whose minute this was.

    Required on every call. If you cannot name the learner and the concept, PRAXIS has no business
    spending anything.
    """

    learner_id: str
    concept_id: str

    def __post_init__(self) -> None:
        if not self.learner_id.strip():
            raise ValueError(
                "A model call must be attributed to a learner. There is no such thing as a call "
                "PRAXIS makes for its own benefit."
            )

        if not self.concept_id.strip():
            raise ValueError("A model call must be attributed to a concept.")


@dataclass(frozen=True, slots=True)
class Cost:
    """The bill for one call.

    ``usd is None`` means *we do not know what this cost* — a fact the Ledger is entitled to record
    and act on. It never means zero.
    """

    model: str
    input_tokens: int
    output_tokens: int
    usd: Decimal | None

    @property
    def is_priced(self) -> bool:
        return self.usd is not None

    def as_log_fields(self) -> dict[str, object]:
        return {
            "model": self.model,
            "input_tokens": self.input_tokens,
            "output_tokens": self.output_tokens,
            "usd": None if self.usd is None else float(self.usd),
            "priced": self.is_priced,
        }


def price_of(model: str, input_tokens: int, output_tokens: int) -> Cost:
    """What one call cost. Unknown model → unknown cost, and it says so."""
    prices = PRICES.get(model)

    if prices is None:
        return Cost(model, input_tokens, output_tokens, usd=None)

    per_input, per_output = prices

    usd = (Decimal(input_tokens) * per_input + Decimal(output_tokens) * per_output) / _PER_MILLION

    return Cost(model, input_tokens, output_tokens, usd=usd)


def assert_priced(models: set[str]) -> None:
    """Called at startup. A model PRAXIS routes to must have a price *before* it serves anyone."""
    unpriced = sorted(model for model in models if model not in PRICES)

    if unpriced:
        raise UnknownPriceError(
            f"No price for {', '.join(unpriced)}. Every model PRAXIS routes to must be priced in "
            "cost.PRICES — an unpriced model would be billed to a learner at an unknown rate, and "
            "recorded against them as if it were free."
        )
