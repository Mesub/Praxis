"""**The grader, whose most important output is "I don't know".**

Every LLM grader ever shipped will produce a number for anything you hand it. Feed it a blank page,
a poem, a response in a language it half-understands, or an argument that is subtly right in a way
the rubric's author did not anticipate — and it will return `0.73`, with the same serene confidence
it brings to the easy cases. It has no way to say *"I am not sure"*, because nobody gave it one and
because a grader that hesitates looks broken in a demo.

That number then becomes **Evidence**, and Evidence moves what PRAXIS believes about a person, and
what PRAXIS believes about a person decays and compounds and gets built upon. A confidently wrong
grade is not a bad row in a table. It is a false belief about someone's mind, and they will feel it
months later as work that is inexplicably too hard.

So abstention is a **first-class outcome** here (R-1). Three things can come out of this module:

* ``PASSED`` — the learner demonstrated it.
* ``FAILED`` — the learner did not.
* ``PENDING`` — **PRAXIS does not know, and says so.** It goes to a human. It is *never* silently
  resolved in either direction, and in particular it is never resolved *upwards* because a queue got
  long.

`PENDING` is not a failure state and it is not a temporary `PASSED`. It is PRAXIS declining to write
a belief it has not earned — which is precisely the standard it holds learners to.

## The ladder

1. **Deterministic pre-checks, before any model runs.** Where truth is deterministic, we use
   determinism. A response too short to be an explanation, or one containing a hard-fail phrase, is
   settled without spending a judge — and, more importantly, without giving a language model the
   chance to be *talked round* by an eloquent wrong answer.
2. **Three independent judges**, each seeing only the rubric and the response — never the learner's
   identity, their Twin, their previous scores, or whether this is a retry. A judge that knows it is
   marking a struggling learner's fourth attempt is no longer marking the work.
3. **Disagreement escalates.** If the judges are more than half a band apart, an adjudicator sees
   the response *and all three rationales* and decides.
4. **The boundary escalates to a human.** A score within ±0.05 of the contract threshold is exactly
   the case where the grade *changes the decision* — and it is the only case worth a human's time.
   Grading the obvious passes and obvious fails by hand is a waste of a person.
"""

from __future__ import annotations

import json
import logging
from collections.abc import Iterable
from dataclasses import dataclass
from decimal import Decimal
from enum import StrEnum

from praxis_ai.claude import ClaudeClient
from praxis_ai.cost import Attribution, Cost
from praxis_ai.models import Task
from praxis_ai.rubric import MAX_BAND, Rubric

log = logging.getLogger("praxis.ai.grading")

#: How far apart the judges may be before an adjudicator is called, in **bands**.
MAX_JUDGE_SPREAD_BANDS = 0.5

#: A score this close to the contract threshold goes to a human. This is the band where the grade
#: *changes the decision*, and it is the only band where a human's attention is worth spending.
BOUNDARY = 0.05

#: Below this, there are not enough opinions to average. Two judges who agree is a grade. One judge
#: on their own is a guess with a number attached.
MIN_JUDGES = 2

JUDGE_COUNT = 3


class Outcome(StrEnum):
    PASSED = "passed"
    FAILED = "failed"

    PENDING = "pending"
    """**PRAXIS does not know.** A human decides.

    Never coerced. Not to `PASSED` when the queue is long, not to `FAILED` when it is easier, and
    not to either one because a dashboard looked bad. The whole point of the state is that it is
    honest, and a state that gets quietly resolved under pressure is not honest — it is latency.
    """


@dataclass(frozen=True, slots=True)
class JudgeScore:
    """One judge's read. ``bands`` is None when the judge abstained."""

    bands: dict[str, int] | None
    abstained: bool
    cost: Cost

    def score(self, rubric: Rubric) -> float | None:
        return None if self.bands is None else rubric.score_of(self.bands)


@dataclass(frozen=True, slots=True)
class Grade:
    outcome: Outcome

    #: The number, when there is one. `None` on a `PENDING` where no score could be formed at all —
    #: because a `PENDING` with a confident-looking score attached is an invitation to use it.
    score: float | None

    #: Misconception **keys**, from the rubric's declared set. Never free text (M5.3's rule).
    misconceptions: tuple[str, ...]

    #: Why this outcome. For the audit trail and for the human in the queue — not for the learner.
    reason: str

    #: How many judges actually produced a score. Zero means the pre-checks settled it.
    judges_consulted: int

    escalated_to_adjudicator: bool
    escalated_to_human: bool

    usd: Decimal | None

    def __post_init__(self) -> None:
        if self.outcome is Outcome.PASSED and self.escalated_to_human:
            raise ValueError(
                "A grade cannot be PASSED and awaiting a human at the same time. This is the "
                "exact coercion the PENDING state exists to prevent: the queue got long, and "
                "somebody let the borderline cases through because they were 'probably fine'."
            )


class Grader:
    def __init__(self, client: ClaudeClient) -> None:
        self._client = client

    async def grade(self, response: str, rubric: Rubric, attribution: Attribution) -> Grade:
        # ── 1. Determinism first ─────────────────────────────────────────────
        if (settled := _pre_check(response, rubric)) is not None:
            return settled

        # ── 2. Three judges, who know nothing about the learner ──────────────
        judges = [
            await self._judge(response, rubric, attribution, seed=i) for i in range(JUDGE_COUNT)
        ]

        spent = _total(judge.cost for judge in judges)

        scored = [j for j in judges if j.bands is not None]

        # **Abstention is contagious, and that is correct.** If two of three judges looked at this
        # and said "I cannot band it", the honest reading is that the *rubric* did not anticipate
        # this response — not that the one judge who was willing to guess was right.
        if len(scored) < MIN_JUDGES:
            return _pending(
                f"{len(scored)} of {JUDGE_COUNT} judges could band this response. PRAXIS will not "
                "average a guess.",
                misconceptions=(),
                judges=len(judges),
                usd=spent,
            )

        scores = [j.score(rubric) for j in scored]
        spread_bands = (max(scores) - min(scores)) * MAX_BAND   # type: ignore[type-var]

        adjudicated = False

        # ── 3. Disagreement escalates ────────────────────────────────────────
        if spread_bands > MAX_JUDGE_SPREAD_BANDS:
            adjudicated = True

            verdict = await self._adjudicate(response, rubric, scored, attribution)

            spent = _add(spent, verdict.cost.usd)

            if verdict.bands is None:
                return _pending(
                    f"The judges were {spread_bands:.2f} bands apart and the adjudicator declined "
                    "to break the tie.",
                    misconceptions=(),
                    judges=len(scored),
                    usd=spent,
                    adjudicated=True,
                )

            bands = verdict.bands
        else:
            bands = _mean_bands(rubric, [j.bands for j in scored])   # type: ignore[misc]

        score = rubric.score_of(bands)
        misconceptions = tuple(rubric.misconceptions_in(bands))

        # ── 4. The boundary is a human's job ─────────────────────────────────
        if abs(score - rubric.pass_score) <= BOUNDARY:
            return _pending(
                f"{score:.3f} is within {BOUNDARY} of the threshold ({rubric.pass_score}). This is "
                "the case where the grade changes the decision, so a person decides it.",
                misconceptions=misconceptions,
                judges=len(scored),
                usd=spent,
                adjudicated=adjudicated,
                score=score,
            )

        outcome = Outcome.PASSED if score >= rubric.pass_score else Outcome.FAILED

        log.info(
            "graded",
            extra={
                "learner_id": attribution.learner_id,
                "concept_id": attribution.concept_id,
                "rubric_id": rubric.id,
                "outcome": outcome.value,
                "score": round(score, 4),
                "judges": len(scored),
                "adjudicated": adjudicated,
            },
        )

        return Grade(
            outcome=outcome,
            score=score,
            misconceptions=misconceptions,
            reason=f"{len(scored)} judges within {spread_bands:.2f} bands.",
            judges_consulted=len(scored),
            escalated_to_adjudicator=adjudicated,
            escalated_to_human=False,
            usd=spent,
        )

    # ── The judges ───────────────────────────────────────────────────────────

    async def _judge(
        self, response: str, rubric: Rubric, attribution: Attribution, seed: int
    ) -> JudgeScore:
        """One judge.

        Note the arguments. There is no learner id in the prompt, no Twin, no prior score, and no
        "this is their fourth attempt" — a judge that knows it is marking a struggling learner is no
        longer marking the work. ``attribution`` goes to the **cost ledger**, not into the prompt.
        """
        # Dimension order is rotated per judge: position bias is a real, measured LLM-judge failure
        # mode, and three judges reading the same list in the same order share it.
        order = list(rubric.dimensions[seed:] + rubric.dimensions[:seed])

        prompt = (
            f"AUDIENCE: {rubric.audience}\n\n"
            "RUBRIC:\n"
            + "\n\n".join(
                f"{d.id} — {d.probe}\n"
                + "\n".join(f"  {band}: {text}" for band, text in sorted(d.bands.items()))
                for d in order
            )
            + f"\n\nRESPONSE TO GRADE:\n{response}"
        )

        completion = await self._client.complete(
            prompt,
            task=Task.GRADE,
            attribution=attribution,
            system=_JUDGE_SYSTEM,
            max_tokens=512,
        )

        return _parse_judge(completion.text, rubric, completion.cost)

    async def _adjudicate(
        self,
        response: str,
        rubric: Rubric,
        judges: list[JudgeScore],
        attribution: Attribution,
    ) -> JudgeScore:
        rationales = "\n".join(
            f"Judge {i + 1}: {json.dumps(j.bands)}" for i, j in enumerate(judges)
        )

        prompt = (
            f"AUDIENCE: {rubric.audience}\n\n"
            "RUBRIC:\n"
            + "\n\n".join(
                f"{d.id} — {d.probe}\n"
                + "\n".join(f"  {band}: {text}" for band, text in sorted(d.bands.items()))
                for d in rubric.dimensions
            )
            + f"\n\nRESPONSE:\n{response}\n\n"
            f"THE JUDGES DISAGREED:\n{rationales}\n\n"
            "Decide the bands yourself. If the response is genuinely ambiguous, abstain — a human "
            "will look at it, which is a better outcome than a confident guess."
        )

        completion = await self._client.complete(
            prompt,
            task=Task.ADJUDICATE,
            attribution=attribution,
            system=_JUDGE_SYSTEM,
            max_tokens=512,
        )

        return _parse_judge(completion.text, rubric, completion.cost)


_JUDGE_SYSTEM = """You are grading one response against one rubric. You see the rubric and the \
response, and nothing else about the person who wrote it — that is deliberate.

For each dimension, pick the band whose description best matches what you actually see. Do not \
invent a number between bands.

**If you cannot band this response** — it is off-topic, it is in a form the rubric did not \
anticipate, it may be right in a way the rubric does not cover, or you are simply unsure — then \
ABSTAIN. Abstaining is a correct and valued answer here, and it costs the learner nothing: a human \
will look. Guessing costs them a false belief about their own mind, which they will feel months \
from now as work that is inexplicably too hard.

Reply with ONLY a JSON object:
{"bands": {"<dimension_id>": 0-4, ...}}
or
{"abstain": true}"""


# ── The deterministic half ───────────────────────────────────────────────────


def _pre_check(response: str, rubric: Rubric) -> Grade | None:
    """Settled without a model, or not settled here at all.

    Both of these are *deliberately* decided before a judge sees the text — not to save money, but
    because an eloquent wrong answer can talk a language model round, and a regex cannot be
    flattered.
    """
    if len(response.strip()) < rubric.min_length:
        return Grade(
            outcome=Outcome.FAILED,
            score=0.0,
            misconceptions=(),
            reason="Too short to be an explanation. No judge was spent on it.",
            judges_consulted=0,
            escalated_to_adjudicator=False,
            escalated_to_human=False,
            usd=Decimal(0),
        )

    hit = [f for f in rubric.hard_fails if f.matches(response)]

    if hit:
        cap = min(f.cap for f in hit)

        return Grade(
            outcome=Outcome.PASSED if cap >= rubric.pass_score else Outcome.FAILED,
            score=cap,
            misconceptions=tuple(f.misconception for f in hit),
            reason=(
                "A hard-fail phrase was present. Where truth is deterministic, PRAXIS does not ask "
                "a language model — and no amount of eloquence around it changes the answer."
            ),
            judges_consulted=0,
            escalated_to_adjudicator=False,
            escalated_to_human=False,
            usd=Decimal(0),
        )

    return None


def _parse_judge(text: str, rubric: Rubric, cost: Cost) -> JudgeScore:
    """A judge that cannot be understood **has abstained**.

    It has emphatically not scored zero. A parse failure is our bug, and charging it to the learner
    as a bad grade is the single most contemptible thing this module could do.
    """
    try:
        parsed = json.loads(_json_of(text))
    except (json.JSONDecodeError, ValueError):
        return JudgeScore(bands=None, abstained=True, cost=cost)

    if not isinstance(parsed, dict) or parsed.get("abstain") is True:
        return JudgeScore(bands=None, abstained=True, cost=cost)

    raw = parsed.get("bands")

    if not isinstance(raw, dict):
        return JudgeScore(bands=None, abstained=True, cost=cost)

    bands: dict[str, int] = {}

    for dimension in rubric.dimensions:
        value = raw.get(dimension.id)

        # A judge that skipped a dimension, or invented a band outside 0-4, has not graded this
        # response. It does not get to have the parts it *did* fill in averaged into a number.
        if not isinstance(value, int) or isinstance(value, bool) or not 0 <= value <= MAX_BAND:
            return JudgeScore(bands=None, abstained=True, cost=cost)

        bands[dimension.id] = value

    return JudgeScore(bands=bands, abstained=False, cost=cost)


def _json_of(text: str) -> str:
    stripped = text.strip()

    start, end = stripped.find("{"), stripped.rfind("}")

    if start == -1 or end == -1 or end < start:
        raise ValueError("no JSON object in the reply")

    return stripped[start : end + 1]


def _mean_bands(rubric: Rubric, judges: list[dict[str, int]]) -> dict[str, int]:
    """Rounded to a band, because bands are what the rubric defines. A '2.67' is not a thing the
    rubric's author ever described, and pretending otherwise manufactures precision."""
    return {
        d.id: round(sum(j[d.id] for j in judges) / len(judges)) for d in rubric.dimensions
    }


def _pending(
    reason: str,
    *,
    misconceptions: tuple[str, ...],
    judges: int,
    usd: Decimal | None,
    adjudicated: bool = False,
    score: float | None = None,
) -> Grade:
    return Grade(
        outcome=Outcome.PENDING,
        score=score,
        misconceptions=misconceptions,
        reason=reason,
        judges_consulted=judges,
        escalated_to_adjudicator=adjudicated,
        escalated_to_human=True,
        usd=usd,
    )


def _total(costs: Iterable[Cost]) -> Decimal | None:
    """**An unknown price poisons the total, and it should.**

    A sum that treats one unknown component as zero is a number that lies with a straight face. If
    any leg of this grade was billed at a rate nobody knows, the grade's cost is *unknown* — which
    the Ledger can see and act on, unlike a plausible-looking underestimate.
    """
    total = Decimal(0)

    for cost in costs:
        if cost.usd is None:
            return None

        total += cost.usd

    return total


def _add(a: Decimal | None, b: Decimal | None) -> Decimal | None:
    return None if a is None or b is None else a + b
