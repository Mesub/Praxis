"""**The contract is committed, and it must not lie.**

`contracts/ai-plane.openapi.json` is the seam between two languages. The .NET core checks itself
against that file; this test checks the file against the running service. Between them, a change to
the wire format that the C# side has not been told about cannot reach a learner — a build fails
first.

A committed contract that has quietly gone stale is worse than no contract: both sides believe they
agree.
"""

from __future__ import annotations

import json
from pathlib import Path

from praxis_ai.app import app

CONTRACT = Path(__file__).resolve().parents[2] / "contracts" / "ai-plane.openapi.json"


def test_the_committed_contract_matches_the_service() -> None:
    """If this fails: ``uv run python scripts/export_contract.py``, and **look at the diff** — a
    change here is a change to the wire format the .NET core is compiled against."""
    committed = json.loads(CONTRACT.read_text(encoding="utf-8"))

    assert committed == app.openapi(), (
        "The committed OpenAPI contract no longer matches this service. Re-export it — and read "
        "the diff, because the .NET core is compiled against the old one."
    )


def test_the_contract_offers_the_core_exactly_five_things() -> None:
    """Everything the .NET core is allowed to ask an LLM for. If a fifth appears, it was designed
    somewhere other than here.

    Note there is exactly **one** door to the oracle. An earlier draft of the tutor route grew its
    own `/tutor/check` — a second way to reach the answer key. Two doors to one secret is one door
    too many. Deleted.
    """
    committed = json.loads(CONTRACT.read_text(encoding="utf-8"))

    assert set(committed["paths"]) == {
        "/health",
        "/oracle/check-claim",
        "/assessment/grade",
        "/assessment/items",
        "/tutor/turn",
        "/tutor/turn/stream",
    }

    # The oracle has one door, and it is the one the tutor knocks on.
    assert len([p for p in committed["paths"] if "check" in p]) == 1


def test_no_schema_in_the_contract_can_carry_an_answer() -> None:
    """**The sweep, at the seam.**

    The most dangerous place for an ``answer`` field to appear is here — in the shared contract —
    because it would be generated into a C# property, and then it exists on both sides of the wire
    and somebody will fill it in.
    """
    committed = json.loads(CONTRACT.read_text(encoding="utf-8"))

    forbidden = {"answer", "answer_key", "correct_answer", "solution", "expected"}

    for name, schema in committed["components"]["schemas"].items():
        fields = set(schema.get("properties", {}))

        assert not (fields & forbidden), (
            f"{name} carries {fields & forbidden} across the wire. The .NET core can now read an "
            "answer, and PRAXIS's central claim is over."
        )


def test_every_call_names_the_learner_it_is_spending_on() -> None:
    """The AI plane is stateless: it cannot know who is calling unless the call says so. And nothing
    is spent anonymously (M5.2), so *"who is this for?"* is required on every request that costs
    money."""
    committed = json.loads(CONTRACT.read_text(encoding="utf-8"))

    for name in ("CheckClaimRequest", "GradeRequest", "GenerateItemRequest"):
        schema = committed["components"]["schemas"][name]

        assert "learner_id" in schema["properties"], f"{name} spends money anonymously."
        assert "learner_id" in schema["required"], f"{name} makes the learner optional."


def test_pending_is_in_the_contract_so_the_core_cannot_ignore_it() -> None:
    """R-1 crosses the wire. If ``pending`` were not in the enum the C# side is generated from, the
    core would have no way to represent "PRAXIS does not know" — and it would have to pick."""
    committed = json.loads(CONTRACT.read_text(encoding="utf-8"))

    assert set(committed["components"]["schemas"]["GradeOutcome"]["enum"]) == {
        "passed",
        "failed",
        "pending",
    }
