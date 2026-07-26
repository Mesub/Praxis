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
from praxis_ai.oracle import OPTION_COUNT, AnswerKey, InMemoryAnswerKeyStore

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


class ItemFormat(StrEnum):
    """How an item is answered, and therefore how it is marked.

    ``MULTIPLE_CHOICE`` is marked by comparing two integers — no model, no judgement, no cost. It
    is the only marking in PRAXIS whose verdict is a *fact* rather than an opinion.
    """

    PROSE = "prose"
    MULTIPLE_CHOICE = "multiple_choice"


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

    #: The text under study, for a reading item. The learner reads it; it is not a secret.
    #:
    #: Placed after ``difficulty`` rather than immediately after ``stem``: every field before this
    #: one has no default, and a dataclass cannot put a defaulted field ahead of one that still
    #: requires a value.
    passage: str | None = None

    #: The choices as shown, in order. **Not a secret** — they are on the screen. Which one is
    #: correct is not here, and there is no field it could be put in.
    options: tuple[str, ...] | None = None

    #: A stable fingerprint of the stem, so "have they seen this?" is a set lookup rather than an
    #: opinion.
    fingerprint: str = field(compare=False, default="")

    #: The **key** of a declared wrong belief this item was built to catch, or ``None``. A key from
    #: the caller's own declared set (never free text the model invented) — the airlock in
    #: ``_parse`` guarantees that, the same discipline as the oracle's ``_parse``.
    targets_misconception: str | None = field(compare=False, default=None)


def fingerprint_of(stem: str, passage: str | None = None) -> str:
    """A stem's identity, robust to the cosmetic edits a model makes when told to 'vary' something.

    Lowercased, punctuation stripped, whitespace collapsed. Two stems that differ only in phrasing
    still collide — which is the entire point, because a model handed "make it novel" will change
    the punctuation and the names and consider the job done.

    When there is a passage it is part of the identity — otherwise a generator could hand back the
    same passage with a reworded question forever and every one would look new. With no passage the
    input is the stem alone, byte for byte as before, so **every fingerprint already banked stays
    valid**.
    """
    subject = stem if passage is None else f"{passage}\n{stem}"
    normalised = re.sub(r"[^a-z0-9\s]", "", subject.lower())
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
        known_misconceptions: dict[str, str] | None = None,
        item_format: ItemFormat = ItemFormat.PROSE,
        attempts: int = 3,
    ) -> Item:
        """A never-seen item, and an answer key filed where the tutor cannot reach it.

        ``already_seen`` is a set of **fingerprints**, not stems. The caller (the .NET core, which
        actually remembers things) knows what this learner has met; this process does not, and does
        not need to.

        ``known_misconceptions`` is the concept's **declared** wrong beliefs, keyed by a short
        stable slug. The generator is told what they are and may name one in ``targets`` — but only
        a key from this map ever survives (M14.2). Free text the model invents is discarded, never
        passed through. A ``MULTIPLE_CHOICE`` item may be asked for with no declared beliefs at all
        (``None`` or ``{}``) — a distractor need not name a diagnosis to be a valid wrong answer.

        ``item_format`` chooses the shape: ``PROSE`` (the default) asks for a question and an answer
        a marker can compare against; ``MULTIPLE_CHOICE`` asks for a passage and four options as
        well, and the returned ``Item`` carries them — but never which one is correct.
        """
        if not 0.0 <= difficulty <= 1.0:
            raise ValueError(f"Difficulty is 0-1. {difficulty} is not a difficulty.")

        last: str = ""

        for attempt in range(attempts):
            item, key, cost = await self._draft(
                concept_id,
                concept_summary,
                evidence_type,
                difficulty,
                attribution,
                already_seen,
                known_misconceptions,
                item_format,
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
        known_misconceptions: dict[str, str] | None,
        item_format: ItemFormat,
    ) -> tuple[Item, AnswerKey, Cost]:
        system = _SYSTEM_MULTIPLE_CHOICE if item_format is ItemFormat.MULTIPLE_CHOICE else _SYSTEM

        completion = await self._client.complete(
            _prompt(
                concept_summary, evidence_type, difficulty, len(already_seen), known_misconceptions
            ),
            task=Task.GENERATE_ITEM,
            attribution=attribution,
            system=system,
            max_tokens=2_048,
        )

        known_keys = frozenset((known_misconceptions or {}).keys())
        drafted = _parse(completion.text, known_keys, item_format)

        passage = drafted.get("passage")
        options = drafted.get("options")

        item_id = f"item-{fingerprint_of(drafted['question'], passage)[:16]}"

        targeted = drafted["targets"]

        item = Item(
            id=item_id,
            concept_id=concept_id,
            evidence_type=evidence_type,
            stem=drafted["question"],
            difficulty=difficulty,
            fingerprint=fingerprint_of(drafted["question"], passage),
            targets_misconception=targeted,
            passage=passage,
            options=None if options is None else tuple(options),
        )

        # The union: the generator's own catches, plus the declared statement for whichever known
        # belief this item was built to catch — so the oracle can recognise it at marking time even
        # if the generator's own ``misconceptions`` map did not restate it.
        misconceptions = dict(drafted["misconceptions"])

        if targeted is not None and known_misconceptions is not None:
            misconceptions.setdefault(targeted, known_misconceptions[targeted])

        key = AnswerKey(
            item_id=item_id,
            concept_id=concept_id,
            question=drafted["question"],
            answer=drafted["answer"],
            misconceptions=misconceptions,
            correct_option=drafted.get("correct"),
            option_misconceptions=drafted.get("option_misconceptions", {}),
        )

        return item, key, completion.cost


_SYSTEM = """You write assessment items. You produce a question, its answer, the misconceptions \
the question is designed to catch, and — when you are given a concept's already-known wrong \
beliefs — which one, if any, this item is built to catch.

The question will be shown to a learner. The answer will NOT be — it goes to a marking oracle that \
the tutor cannot read. Write the answer for a marker, not for a student: it is a marking standard, \
not an explanation.

Rules:
- The question must be answerable from understanding, not from recognition.
- For APPLY items: a problem the learner has NEVER seen, that cannot be solved by pattern-matching \
a worked example. This is not a stylistic preference — an APPLY item that can be answered by \
recognising a template is not evidence of anything.
- Misconceptions are keyed by a short stable slug (kebab-case), e.g. "thinks-tf-is-linear".
- ``targets`` names the key of one of the KNOWN WRONG BELIEFS given to you, if this item is built \
to catch it — or null if none was given, or none fits. Only a key from that declared list is \
valid; never invent one.

Reply with ONLY a JSON object:
{"question": "...", "answer": "...", "misconceptions": {"slug": "the wrong belief, stated"}, \
"targets": "<declared key>" | null}

Do not explain. Do not add commentary. The JSON object is your entire reply."""


_SYSTEM_MULTIPLE_CHOICE = """You write reading-comprehension items. You produce a short passage, \
one question about it, exactly four options of which exactly one is correct, the index of that \
option, and — for each wrong option — which of the reader's known wrong habits it is built to catch.

The passage and the options will be shown to a learner. Which option is correct will NOT be: it \
goes to a marking oracle the tutor cannot read.

Rules:
- The passage is self-contained, 80-150 words, everyday or workplace English. Everything needed to \
answer is in it. Never refer to anything outside it.
- The question must be answerable ONLY by understanding the passage. A reader who has not read it \
must not be able to guess from general knowledge.
- Exactly four options. Exactly one correct. All four plausible to someone who misread; an option \
nobody would pick is a wasted option.
- Each wrong option should be built to catch a specific KNOWN WRONG HABIT from the list you are \
given, named by its key in `option_misconceptions`. Only keys from that list are valid. If you are \
given no known wrong habits, write plausible distractors anyway and leave `option_misconceptions` \
empty — a distractor does not need a named diagnosis to be a valid wrong answer.
- `correct` is the 0-based index of the correct option.

Reply with ONLY a JSON object:
{"passage": "...", "question": "...", "options": ["...", "...", "...", "..."], "correct": 0, \
"option_misconceptions": {"1": "<declared key>"}, "answer": "the correct option's text", \
"misconceptions": {"slug": "the wrong belief, stated"}, "targets": "<declared key>" | null}

Do not explain. Do not add commentary. The JSON object is your entire reply."""


def _prompt(
    summary: str,
    evidence_type: EvidenceType,
    difficulty: float,
    seen: int,
    known: dict[str, str] | None = None,
) -> str:
    novelty = (
        f"\n\nThis learner has already been shown {seen} item(s) on this concept. Yours must be "
        "genuinely different — a different situation, not the same problem with the numbers "
        "changed. It will be checked."
        if seen
        else ""
    )

    beliefs = "\n".join(f"- {k}: {v}" for k, v in (known or {}).items())
    targeting = (
        f"\n\nKNOWN WRONG BELIEFS for this concept (key: the belief):\n{beliefs}\n"
        "If your item is built to catch one of these, name its key in `targets`. "
        "Only these keys are valid; anything else will be discarded."
        if known
        else ""
    )

    return (
        f"CONCEPT:\n{summary}\n\n"
        f"EVIDENCE TYPE: {evidence_type.value}\n"
        f"DIFFICULTY TARGET: {difficulty:.2f} (0 = trivial, 1 = expert){novelty}{targeting}"
    )


def _parse(
    text: str, known_keys: frozenset[str], item_format: ItemFormat = ItemFormat.PROSE
) -> dict:
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

    # **The airlock** — same discipline as the oracle's ``_parse``. ``targets`` is a key the caller
    # declared *in advance*, or nothing at all. A model that names a slug it invented on the spot
    # gets logged and discarded, never passed through as if it were real.
    targets = parsed.get("targets")

    if targets is not None and targets not in known_keys:
        log.warning(
            "Generator proposed a misconception target outside the declared set. Discarded.",
            extra={"targets": targets},
        )
        targets = None

    if item_format is not ItemFormat.MULTIPLE_CHOICE:
        return {
            "question": question.strip(),
            "answer": answer.strip(),
            "misconceptions": {k.strip(): v.strip() for k, v in misconceptions.items()},
            "targets": targets,
        }

    # A reading item, on top of everything checked above: the answer key (``answer``, checked
    # already) is still filed as the correct option's text, so the store's ``answer NOT NULL``
    # column never goes empty for a fixed-key item either.
    passage = parsed.get("passage")
    options = parsed.get("options")
    correct = parsed.get("correct")

    if not isinstance(passage, str) or not passage.strip():
        raise ItemRejectedError("A reading item with no passage is not a reading item.")

    if (
        not isinstance(options, list)
        or len(options) != OPTION_COUNT
        or not all(isinstance(o, str) and o.strip() for o in options)
    ):
        raise ItemRejectedError(
            f"A fixed-key item needs exactly {OPTION_COUNT} non-empty options. Fewer is a "
            "different item with a different guessing floor, and the estimator was told to expect "
            "four."
        )

    if not isinstance(correct, int) or isinstance(correct, bool) or not 0 <= correct < OPTION_COUNT:
        raise ItemRejectedError(
            "The correct option must be an index into the four options. Without it the item is "
            "unmarkable, and a learner would spend real effort on it for nothing."
        )

    raw_catches = parsed.get("option_misconceptions")

    # Absent or null means "no distractor beliefs" — the common case, since 7 of 15 Reading
    # English concepts declare no misconceptions at all. Present and not a map is a different
    # problem: we cannot tell which option it was trying to describe, and every other malformed
    # field in this function costs a retry rather than the process, so this one does too.
    if raw_catches is not None and not isinstance(raw_catches, dict):
        raise ItemRejectedError(
            "Option misconceptions must be a map of option index to a declared belief key. What "
            "came back cannot be read as one, and guessing at its shape risks attributing a wrong "
            "belief nobody wrote down."
        )

    # The airlock, again: a distractor may only name a belief the caller declared in advance. A
    # fixed-key item may declare none at all — an empty ``known_keys`` is a supported case, not an
    # error, and every proposed catch is simply discarded rather than raising.
    catches: dict[int, str] = {}

    for raw_index, key in (raw_catches or {}).items():
        try:
            index = int(raw_index)
        except (TypeError, ValueError):
            continue

        if index == correct or not 0 <= index < OPTION_COUNT:
            continue

        if key not in known_keys:
            log.warning(
                "Generator proposed a distractor belief outside the declared set. Discarded.",
                extra={"key": key},
            )
            continue

        catches[index] = key

    return {
        "question": question.strip(),
        "answer": answer.strip(),
        "misconceptions": {k.strip(): v.strip() for k, v in misconceptions.items()},
        "targets": targets,
        "passage": passage.strip(),
        "options": [o.strip() for o in options],
        "correct": correct,
        "option_misconceptions": catches,
    }


def _json_of(text: str) -> str:
    stripped = text.strip()

    start, end = stripped.find("{"), stripped.rfind("}")

    if start == -1 or end == -1 or end < start:
        raise ValueError("no JSON object in the reply")

    return stripped[start : end + 1]
