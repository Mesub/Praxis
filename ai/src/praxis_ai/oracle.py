"""**The oracle. The architectural heart of the refusal.**

PRAXIS promises that the tutor cannot give you the answer. Most products that promise this implement
it as an *instruction*: the answer sits in the system prompt, wrapped in capital letters begging the
model not to say it. That is not a guarantee. That is a lock with the key taped to the door, and
every week someone publishes a new way to peel the tape off.

PRAXIS implements it as an **absence**. The answer is never placed in the tutor's context at all
(C-5). The tutor cannot be talked out of the answer, bribed for it, or tricked into roleplaying a
character who knows it, because **there is nothing there**. A jailbreak retrieves what is present,
and what is present is nothing.

The tutor instead calls *this*, and this holds the key.

## Which moves the problem rather than solving it — so read on

An oracle is a channel, and every channel leaks. If it answered *"wrong — you are missing two of the
four required elements"*, a learner with patience (or a tutor that has been jailbroken into acting
as a proxy) can grind guesses against it and **binary-search the answer out**. The oracle would have
become a slow, polite way of giving the answer away, and everybody involved would have been able to
say, truthfully, that the answer was never in the prompt.

So two things hold the line:

1. **The verdict is drawn from a closed vocabulary fixed by the item's author, in advance.** The
   model that compares the claim to the answer *does* see the answer — it must — but **not one token
   of its free text ever escapes this module**. It picks from an enumeration; the enumeration is
   validated against the item's declared set; anything else is discarded. There is no field on the
   returned ``Verdict`` in which a sentence could hide, which is a property you can check by reading
   the type, not by trusting the prompt.

2. **The number of questions is finite.** That is the Answer Budget (M6), and it is why the
   budget is not a pricing gimmick but a *security control*.
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from enum import StrEnum
from typing import Protocol

from praxis_ai.claude import ClaudeClient
from praxis_ai.cost import Attribution, Cost
from praxis_ai.models import Task

log = logging.getLogger("praxis.ai.oracle")


class ClaimOutcome(StrEnum):
    """The whole vocabulary. Three words, and no room in any of them for a sentence."""

    CORRECT = "correct"
    PARTIAL = "partial"
    WRONG = "wrong"


class UnknownItemError(KeyError):
    """No key for this item. PRAXIS refuses rather than guessing at what the answer might be."""


class OracleUnclearError(RuntimeError):
    """The model's reply was not a verdict.

    Deliberately **not** defaulted to ``WRONG``. A learner must never be marked wrong because our
    parser failed — that would write a false belief into a model of their mind on the strength of
    our own bug. The tutor says "I could not check that, say it another way", which is embarrassing
    for us and harmless to them.
    """


@dataclass(frozen=True, slots=True)
class AnswerKey:
    """What the item is actually asking for. **This never leaves this module.**"""

    item_id: str
    concept_id: str
    question: str

    #: The answer. The only string in PRAXIS that is genuinely dangerous.
    answer: str

    #: The misconceptions this item is designed to catch, keyed by a **short stable slug**. The
    #: statement is here so the comparing model can recognise the error; only the *key* is ever
    #: returned, and only if it is one of these.
    misconceptions: dict[str, str]


class AnswerKeyStore(Protocol):
    """Where the keys live.

    A protocol, not a table, because the storage decision belongs to M5.5/M5.7 — but the *shape* is
    already load-bearing: there is a ``get``, and there is no ``list``, no ``search``, and nothing
    that hands out an answer to anybody but the oracle itself.
    """

    def get(self, item_id: str) -> AnswerKey | None: ...


class InMemoryAnswerKeyStore:
    """For tests and for the seed pack. M5.5 writes generated keys here."""

    def __init__(self, keys: dict[str, AnswerKey] | None = None) -> None:
        self._keys = dict(keys or {})

    def add(self, key: AnswerKey) -> None:
        self._keys[key.item_id] = key

    def get(self, item_id: str) -> AnswerKey | None:
        return self._keys.get(item_id)


@dataclass(frozen=True, slots=True)
class Verdict:
    """**Everything the tutor is ever told.**

    Look at what is not here. No explanation. No "you were close". No list of missing elements. No
    correct-answer field, not even an optional one, not even a nullable one that is "only populated
    after the budget is exhausted" — because the moment such a field exists, something will populate
    it, and the guarantee becomes a code path rather than a fact about the type.

    ``misconception`` is a **key** from a set the item's author wrote down in advance. It is not
    free text and it cannot be made into free text.
    """

    outcome: ClaimOutcome
    misconception: str | None
    cost: Cost

    def __post_init__(self) -> None:
        if self.misconception is not None and not self.misconception.strip():
            raise ValueError(
                "A misconception is a key or it is nothing. It is never an empty hint."
            )


_SYSTEM = """You are a marking oracle. You are given a question, the correct answer, and a claim \
made by a learner. Decide whether the learner's claim is correct, partially correct, or wrong.

You may also identify which known misconception the claim matches, if any.

Reply with ONLY a JSON object, and nothing else:
{"outcome": "correct" | "partial" | "wrong", "misconception": <key from the list> | null}

Do not explain. Do not restate the answer. Do not address the learner. Your entire reply is the \
JSON object."""


class Oracle:
    """Holds the answer. Tells you three words about it."""

    def __init__(self, client: ClaudeClient, store: AnswerKeyStore) -> None:
        self._client = client
        self._store = store

    async def check(self, item_id: str, claim: str, attribution: Attribution) -> Verdict:
        key = self._store.get(item_id)

        if key is None:
            raise UnknownItemError(
                f"No answer key for item {item_id!r}. PRAXIS will not improvise a verdict about a "
                "learner from an item it cannot mark."
            )

        # **The one place in PRAXIS where the answer enters a prompt.** It is here, it is in this
        # function, and it is nowhere else. The tutor's prompt is assembled in a different module
        # that cannot reach the store, and this call's output is discarded except for two enumerated
        # fields.
        prompt = _prompt(key, claim)

        completion = await self._client.complete(
            prompt,
            task=Task.CHECK_CLAIM,   # heavy tier: this decides what PRAXIS believes about a person
            attribution=attribution,
            system=_SYSTEM,
            max_tokens=128,          # a verdict is 30 tokens. There is no room to smuggle an essay.
        )

        outcome, misconception = _parse(completion.text, key)

        # Note what is logged, and what is not: no claim, no answer, no model output. Just the
        # verdict, which is three words the learner already knows.
        log.info(
            "claim checked",
            extra={
                "item_id": item_id,
                "concept_id": key.concept_id,
                "learner_id": attribution.learner_id,
                "outcome": outcome.value,
                "misconception": misconception,
            },
        )

        return Verdict(outcome=outcome, misconception=misconception, cost=completion.cost)


def _prompt(key: AnswerKey, claim: str) -> str:
    known = "\n".join(f"- {slug}: {statement}" for slug, statement in key.misconceptions.items())

    return (
        f"QUESTION:\n{key.question}\n\n"
        f"CORRECT ANSWER:\n{key.answer}\n\n"
        f"KNOWN MISCONCEPTIONS (use the key on the left, or null):\n{known or '- (none)'}\n\n"
        f"LEARNER'S CLAIM:\n{claim}"
    )


def _parse(text: str, key: AnswerKey) -> tuple[ClaimOutcome, str | None]:
    """**The airlock.**

    Everything the model said arrives here, and only two enumerated values leave. A model that
    ignores its instructions and replies with the answer in prose produces an
    ``OracleUnclearError``, not a leak — and a model that tries to smuggle the answer *inside* the
    misconception field finds that the field only accepts keys the item's author wrote down before
    the learner existed.
    """
    try:
        parsed = json.loads(_json_of(text))
    except (json.JSONDecodeError, ValueError) as bad:
        raise OracleUnclearError(
            "The oracle's reply was not a verdict. The learner is told PRAXIS could not check the "
            "claim — never that they were wrong, which would be blaming them for our bug."
        ) from bad

    if not isinstance(parsed, dict):
        raise OracleUnclearError("The oracle's reply was not a verdict object.")

    try:
        outcome = ClaimOutcome(str(parsed.get("outcome", "")).strip().lower())
    except ValueError as bad:
        raise OracleUnclearError(
            f"{parsed.get('outcome')!r} is not one of {[o.value for o in ClaimOutcome]}."
        ) from bad

    misconception = parsed.get("misconception")

    # The closed vocabulary. A key the item's author declared, or nothing at all. There is no third
    # option, and in particular there is no "pass it through and let the tutor decide".
    if misconception is not None and misconception not in key.misconceptions:
        log.warning(
            "The oracle proposed a misconception outside the item's declared set. Discarded.",
            extra={"item_id": key.item_id},
        )
        misconception = None

    return outcome, misconception


def _json_of(text: str) -> str:
    """Models sometimes wrap JSON in a fence. That is the only latitude they get."""
    stripped = text.strip()

    if stripped.startswith("```"):
        stripped = stripped.split("```")[1] if "```" in stripped[3:] else stripped[3:]
        stripped = stripped.removeprefix("json").strip()

    start, end = stripped.find("{"), stripped.rfind("}")

    if start == -1 or end == -1 or end < start:
        raise ValueError("no JSON object in the reply")

    return stripped[start : end + 1]
