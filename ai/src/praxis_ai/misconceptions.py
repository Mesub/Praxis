"""**Turning a wrong answer into a bug report on a human mind.**

## The prompt is the whole trick

The obvious thing to ask a model is *"what did this learner get wrong?"* — and the answer comes back
useless every time:

    "They said that support levels cannot be broken."

That is a **restatement of the error**. You cannot teach against it, you cannot generate an item
against it, and you certainly cannot watch it die. It tells you what happened; it tells you nothing
about *why*.

So PRAXIS asks a different question:

    "What RULE would PRODUCE this answer?"

    "They believe a price level is a physical barrier rather than a finite pool of liquidity."

**The first is an error. The second is a bug report on a human mind.** Only the second is targetable
— you can build a contrasting case against it, inject it as the flaw in a CRITIQUE item, and watch
whether the learner ever falls for it again.

The entire misconception engine (I-6) rests on asking the second question rather than the first.

## And the discipline that keeps it honest

A learner's wrong answer is *their* text, not an answer key — so unlike the oracle (M5.3), free text
here is not dangerous. But an extractor left to invent freely produces a hundred slightly different
phrasings of the same wrong belief, and a misconception that cannot be *matched* across learners
cannot be mined, cannot be named, and cannot be built into an item bank.

So the extractor is asked to match against the concept's **declared library** first, and only
proposes something new when nothing fits. What it proposes is **learner-local** and stays that way
until enough other people turn out to hold it too.

> **The global library does not get polluted by n=1 noise.**
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass

from praxis_ai.claude import ClaudeClient
from praxis_ai.cost import Attribution, Cost
from praxis_ai.models import Task

log = logging.getLogger("praxis.ai.misconceptions")

#: A candidate must be held by this many *different* learners before it is even considered for the
#: global library. Below it the belief is real for that one person, and is targeted for them — but
#: it is not yet a fact about the world.
MIN_LEARNERS_TO_PROMOTE = 25

#: And a human curator still has to approve it. **No misconception enters the global library because
#: a model was confident.**
REQUIRES_HUMAN_CURATOR = True


class NotAMisconceptionError(RuntimeError):
    """The extractor could not name a rule that would produce this answer.

    **This is a normal outcome and it is important.** Sometimes a person is just wrong — a slip, a
    typo, a misread, a bad afternoon. There is no coherent wrong *rule* underneath it, and inventing
    one would mean PRAXIS teaching against a belief the learner does not hold.

    A misconception engine that always finds a misconception is a horoscope.
    """


@dataclass(frozen=True, slots=True)
class KnownMisconception:
    """An entry in the concept's declared library."""

    key: str

    #: The wrong belief, **in the learner's voice**. First person, on purpose: it has to be
    #: recognisable to them, or they will not believe they hold it.
    statement: str


@dataclass(frozen=True, slots=True)
class Extraction:
    """What rule would produce this answer."""

    #: A key from the concept's declared library, or `None` when nothing matched.
    key: str | None

    #: The wrong **rule**, stated as a rule. Not *"they said X"* — *"they believe Y"*.
    rule: str

    #: True when this did not match anything known. **Usable immediately for this learner** — PRAXIS
    #: can target a belief it has not named globally — but it does not touch the global library.
    is_learner_local: bool

    cost: Cost

    @property
    def is_known(self) -> bool:
        return self.key is not None


_SYSTEM = """You are diagnosing a wrong answer.

**Do not tell me what the learner got wrong.** That is obvious and it is useless. Telling me "they \
said support cannot be broken" describes the error; it does not explain it, and I cannot teach \
against it.

Tell me instead: **what RULE would PRODUCE this answer?** What would somebody have to believe, for \
this answer to be the reasonable thing to write? State it as a belief they hold, in one sentence, \
in their own voice.

  Bad:  "They said a price level can't be broken."
  Good: "A price level is a physical barrier, not a finite pool of liquidity that gets used up."

Then check it against the known misconceptions I give you. If it is one of them, say which. If it \
genuinely is not, say so and give me the new rule.

**If there is no coherent wrong rule** -- they made a slip, misread the question, or simply do not \
know -- then say so. Do not invent a belief. A learner who has made a typo does not need to be \
taught out of a misconception they do not hold.

Reply with ONLY a JSON object:
{"rule": "...", "key": "<known key>" | null, "no_misconception": true | false}"""


class MisconceptionExtractor:
    def __init__(self, client: ClaudeClient) -> None:
        self._client = client

    async def extract(
        self,
        *,
        question: str,
        their_answer: str,
        known: list[KnownMisconception],
        attribution: Attribution,
    ) -> Extraction:
        """Ask what rule would produce this answer.

        Note what is **not** passed in: the correct answer. The extractor does not need it — it is
        being asked to reason *forwards* from the learner's answer to the belief that generated it,
        not backwards from the truth. Keeping the key out of here means one fewer place it exists.
        """
        library = "\n".join(f"- {m.key}: {m.statement}" for m in known) or "- (none declared)"

        prompt = (
            f"QUESTION:\n{question}\n\n"
            f"WHAT THE LEARNER WROTE:\n{their_answer}\n\n"
            f"KNOWN MISCONCEPTIONS FOR THIS CONCEPT:\n{library}"
        )

        completion = await self._client.complete(
            prompt,
            task=Task.GRADE,   # a judgement about a learner: heavy tier (M5.2)
            attribution=attribution,
            system=_SYSTEM,
            max_tokens=256,
        )

        parsed = _parse(completion.text)

        if parsed.get("no_misconception") is True:
            raise NotAMisconceptionError(
                "There is no coherent wrong rule behind this answer. Sometimes a person is just "
                "wrong — a slip, a misread, a bad afternoon — and PRAXIS will not teach them out "
                "of a belief they do not hold."
            )

        rule = str(parsed.get("rule", "")).strip()

        if not rule:
            raise NotAMisconceptionError("The extractor named no rule.")

        # The closed-vocabulary discipline: a key is only accepted if the concept actually declared
        # it. A model inventing keys would fragment the library into synonyms and make the whole
        # thing unminable.
        proposed = parsed.get("key")
        declared = {m.key for m in known}

        key = proposed if isinstance(proposed, str) and proposed in declared else None

        if proposed is not None and key is None:
            log.warning(
                "The extractor proposed a misconception key that is not in the concept's library. "
                "Treated as learner-local.",
                extra={"concept_id": attribution.concept_id},
            )

        log.info(
            "misconception extracted",
            extra={
                "learner_id": attribution.learner_id,
                "concept_id": attribution.concept_id,
                "key": key,
                "learner_local": key is None,
            },
        )

        return Extraction(
            key=key,
            rule=rule,
            is_learner_local=key is None,
            cost=completion.cost,
        )


def can_promote(distinct_learners: int, coherence: float) -> bool:
    """**The global library does not get polluted by n=1 noise.**

    A candidate is real for the learner who produced it, and PRAXIS targets it for them at once —
    *we can hunt a belief we have not named*. But it does not become a fact about the world until
    enough other people turn out to hold it, and it does not enter the global library until a
    **human being** has read it and agreed.

    No misconception is promoted because a model was confident.
    """
    return distinct_learners >= MIN_LEARNERS_TO_PROMOTE and coherence >= 0.70


def _parse(text: str) -> dict:
    stripped = text.strip()

    start, end = stripped.find("{"), stripped.rfind("}")

    if start == -1 or end == -1 or end < start:
        raise NotAMisconceptionError("The extractor's reply was not a diagnosis.")

    try:
        parsed = json.loads(stripped[start : end + 1])
    except json.JSONDecodeError as bad:
        raise NotAMisconceptionError("The extractor's reply was not a diagnosis.") from bad

    if not isinstance(parsed, dict):
        raise NotAMisconceptionError("The extractor's reply was not a diagnosis object.")

    return parsed
