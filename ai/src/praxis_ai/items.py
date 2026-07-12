"""The item generator: a question the learner has never seen, and an answer they will never see.

## Two separations, and the whole milestone is in them

**1. The answer goes to the oracle's store, not down the return channel.**

The generating model necessarily produces the question *and* its answer in one breath — it cannot
write a good question without knowing what a good answer looks like. The moment after, those two
things must part company forever: the ``Item`` goes back to the caller (and thence to the tutor, and
to the learner), and the ``AnswerKey`` goes **straight into the oracle's store** and is never
returned to anybody.

This is enforced by the *type*, not by a convention. ``generate()`` returns an ``Item``, and an
``Item`` has no field an answer could sit in. There is no ``include_answer=True``. A caller who
wants the answer has nowhere to put it.

**2. "Never-seen" is verified, not requested.**

I-2 requires that `APPLY` evidence come from an item the learner has genuinely never met. You cannot
get that by asking a model nicely. Models repeat themselves — reliably, and especially on the
concepts where the canonical example is famous — and a model that has been told "make it novel" will
cheerfully hand you the same problem with the numbers changed and call it new.

So novelty is **checked, deterministically, by hashing the normalised stem** against what this
learner has already been shown. A collision is a refusal, not a warning. The prompt still *asks* for
novelty, because that improves the odds — but the guarantee is the hash, because the ask is not one.
"""

from __future__ import annotations

import hashlib
import json
import logging
import re
from dataclasses import dataclass, field
from enum import StrEnum

from praxis_ai.claude import ClaudeClient
from praxis_ai.cost import Attribution, Cost
from praxis_ai.models import Task
from praxis_ai.oracle import AnswerKey, InMemoryAnswerKeyStore

log = logging.getLogger("praxis.ai.items")


class EvidenceType(StrEnum):
    """The six kinds of proof (I-2). Mirrors ``Praxis.Shared.EvidenceType`` exactly.

    **They are not equal, and the difference is the product.**
    """

    RECALL = "recall"
    APPLY = "apply"
    EXPLAIN = "explain"
    BUILD = "build"
    CRITIQUE = "critique"
    PREDICT = "predict"


#: The Evidence Ceiling (C-4), as a set. Only these can push a concept into HELD, because each is
#: graded by something a language model cannot talk its way past: a never-seen problem, a test
#: suite, or reality itself.
CAN_CROSS_THE_HOLD_THRESHOLD = frozenset(
    {EvidenceType.APPLY, EvidenceType.BUILD, EvidenceType.PREDICT}
)


class ItemRejectedError(RuntimeError):
    """The generator produced something PRAXIS will not show a learner, and it is discarded.

    A rejected item costs us a retry. A *bad* item costs the learner a false belief about their own
    mind, which is not a trade PRAXIS makes.
    """


@dataclass(frozen=True, slots=True)
class Item:
    """**Everything the tutor and the learner ever see.**

    Read the fields. There is no ``answer``. There is no ``solution``, no ``expected``, no
    ``rubric``, and no ``worked_example`` — not even a nullable one "for the review screen", because
    the moment such a field exists something will populate it and the tutor can read it.

    The answer for this item exists. It is in the oracle's store, behind ``check_claim`` (M5.3), and
    the only thing that will ever come back out of it is one of three words.
    """

    id: str
    concept_id: str
    evidence_type: EvidenceType

    #: What the learner is asked. The *only* text that reaches them.
    stem: str

    #: 0-1. What the generator aimed at, not what it achieved: the Twin finds out the truth from
    #: how learners actually do on it.
    difficulty: float

    #: A stable fingerprint of the stem, so "have they seen this?" is a set lookup rather than an
    #: opinion.
    fingerprint: str = field(compare=False, default="")


def fingerprint_of(stem: str) -> str:
    """A stem's identity, robust to the cosmetic edits a model makes when told to 'vary' something.

    Lowercased, punctuation stripped, whitespace collapsed. Two stems that differ only in phrasing
    still collide — which is the entire point, because a model handed "make it novel" will change
    the punctuation and the names and consider the job done.
    """
    normalised = re.sub(r"[^a-z0-9\s]", "", stem.lower())
    normalised = re.sub(r"\s+", " ", normalised).strip()

    return hashlib.sha256(normalised.encode()).hexdigest()[:32]


class ItemGenerator:
    def __init__(self, client: ClaudeClient, store: InMemoryAnswerKeyStore) -> None:
        self._client = client
        self._store = store

    async def generate(
        self,
        *,
        concept_id: str,
        concept_summary: str,
        evidence_type: EvidenceType,
        difficulty: float,
        attribution: Attribution,
        already_seen: frozenset[str] = frozenset(),
        attempts: int = 3,
    ) -> Item:
        """A never-seen item, and an answer key filed where the tutor cannot reach it.

        ``already_seen`` is a set of **fingerprints**, not stems. The caller (the .NET core, which
        actually remembers things) knows what this learner has met; this process does not, and does
        not need to.
        """
        if not 0.0 <= difficulty <= 1.0:
            raise ValueError(f"Difficulty is 0-1. {difficulty} is not a difficulty.")

        last: str = ""

        for attempt in range(attempts):
            item, key, cost = await self._draft(
                concept_id, concept_summary, evidence_type, difficulty, attribution, already_seen
            )

            # **The check that makes "never-seen" a fact.** The model was asked for novelty; asking
            # is not a guarantee, and this is.
            if item.fingerprint in already_seen:
                last = "the model reproduced an item this learner has already been shown"

                log.warning(
                    "Generated item was not novel. Discarded.",
                    extra={
                        "concept_id": concept_id,
                        "attempt": attempt + 1,
                        "fingerprint": item.fingerprint,
                    },
                )
                continue

            # The parting of the ways. The key goes one way...
            self._store.add(key)

            log.info(
                "item generated",
                extra={
                    "item_id": item.id,
                    "concept_id": concept_id,
                    "evidence_type": evidence_type.value,
                    "learner_id": attribution.learner_id,
                    "usd": None if cost.usd is None else float(cost.usd),
                },
            )

            # ...and the item goes the other, with no way to get from one to the other except by
            # asking the oracle a question it will answer in three words.
            return item

        raise ItemRejectedError(
            f"Could not generate a novel {evidence_type.value} item for {concept_id!r} in "
            f"{attempts} attempts: {last}. PRAXIS will not show a learner a question it knows they "
            "have already seen and call it new — that is exactly the fraud I-2 exists to prevent."
        )

    async def _draft(
        self,
        concept_id: str,
        concept_summary: str,
        evidence_type: EvidenceType,
        difficulty: float,
        attribution: Attribution,
        already_seen: frozenset[str],
    ) -> tuple[Item, AnswerKey, Cost]:
        completion = await self._client.complete(
            _prompt(concept_summary, evidence_type, difficulty, len(already_seen)),
            task=Task.GENERATE_ITEM,
            attribution=attribution,
            system=_SYSTEM,
            max_tokens=2_048,
        )

        drafted = _parse(completion.text)

        item_id = f"item-{fingerprint_of(drafted['question'])[:16]}"

        item = Item(
            id=item_id,
            concept_id=concept_id,
            evidence_type=evidence_type,
            stem=drafted["question"],
            difficulty=difficulty,
            fingerprint=fingerprint_of(drafted["question"]),
        )

        key = AnswerKey(
            item_id=item_id,
            concept_id=concept_id,
            question=drafted["question"],
            answer=drafted["answer"],
            misconceptions=drafted["misconceptions"],
        )

        return item, key, completion.cost


_SYSTEM = """You write assessment items. You produce a question, its answer, and the \
misconceptions the question is designed to catch.

The question will be shown to a learner. The answer will NOT be — it goes to a marking oracle that \
the tutor cannot read. Write the answer for a marker, not for a student: it is a marking standard, \
not an explanation.

Rules:
- The question must be answerable from understanding, not from recognition.
- For APPLY items: a problem the learner has NEVER seen, that cannot be solved by pattern-matching \
a worked example. This is not a stylistic preference — an APPLY item that can be answered by \
recognising a template is not evidence of anything.
- Misconceptions are keyed by a short stable slug (kebab-case), e.g. "thinks-tf-is-linear".

Reply with ONLY a JSON object:
{"question": "...", "answer": "...", "misconceptions": {"slug": "the wrong belief, stated"}}

Do not explain. Do not add commentary. The JSON object is your entire reply."""


def _prompt(summary: str, evidence_type: EvidenceType, difficulty: float, seen: int) -> str:
    novelty = (
        f"\n\nThis learner has already been shown {seen} item(s) on this concept. Yours must be "
        "genuinely different — a different situation, not the same problem with the numbers "
        "changed. It will be checked."
        if seen
        else ""
    )

    return (
        f"CONCEPT:\n{summary}\n\n"
        f"EVIDENCE TYPE: {evidence_type.value}\n"
        f"DIFFICULTY TARGET: {difficulty:.2f} (0 = trivial, 1 = expert){novelty}"
    )


def _parse(text: str) -> dict:
    try:
        parsed = json.loads(_json_of(text))
    except (json.JSONDecodeError, ValueError) as bad:
        raise ItemRejectedError("The generator's reply was not an item.") from bad

    if not isinstance(parsed, dict):
        raise ItemRejectedError("The generator's reply was not an item object.")

    question = parsed.get("question")
    answer = parsed.get("answer")
    misconceptions = parsed.get("misconceptions", {})

    if not isinstance(question, str) or not question.strip():
        raise ItemRejectedError("An item with no question is not an item.")

    # **An item with no answer is worse than useless.** It would reach a learner, they would work on
    # it, and the oracle would have nothing to mark it against — so their effort would be silently
    # discarded, and they would never find out why.
    if not isinstance(answer, str) or not answer.strip():
        raise ItemRejectedError(
            "An item with no answer key cannot be marked. A learner would spend real effort on it "
            "and PRAXIS would have nothing to say back."
        )

    if not isinstance(misconceptions, dict) or not all(
        isinstance(k, str) and isinstance(v, str) for k, v in misconceptions.items()
    ):
        raise ItemRejectedError("Misconceptions must be a map of slug to statement.")

    return {
        "question": question.strip(),
        "answer": answer.strip(),
        "misconceptions": {k.strip(): v.strip() for k, v in misconceptions.items()},
    }


def _json_of(text: str) -> str:
    stripped = text.strip()

    start, end = stripped.find("{"), stripped.rfind("}")

    if start == -1 or end == -1 or end < start:
        raise ValueError("no JSON object in the reply")

    return stripped[start : end + 1]
