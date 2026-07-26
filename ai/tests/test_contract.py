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
from collections.abc import Iterator
from pathlib import Path

from praxis_ai.app import app

CONTRACT = Path(__file__).resolve().parents[2] / "contracts" / "ai-plane.openapi.json"


def _all_property_names(node: object) -> Iterator[str]:
    """Every key that appears in every ``properties`` map in the schema, wherever it is nested.

    A recursive walk, not a substring scan of the serialised JSON — because ``answer`` legitimately
    appears in this document as a **path** (``/oracle/check-claim``) and inside docstrings promoted
    to ``description``/``title`` text. Only a ``properties`` *key* names a field that will actually
    cross the wire as a C# property, so only that is checked.
    """
    if isinstance(node, dict):
        properties = node.get("properties")

        if isinstance(properties, dict):
            yield from properties.keys()

        for value in node.values():
            yield from _all_property_names(value)
    elif isinstance(node, list):
        for item in node:
            yield from _all_property_names(item)


def test_the_committed_contract_matches_the_service() -> None:
    """If this fails: ``uv run python scripts/export_contract.py``, and **look at the diff** — a
    change here is a change to the wire format the .NET core is compiled against."""
    committed = json.loads(CONTRACT.read_text(encoding="utf-8"))

    assert committed == app.openapi(), (
        "The committed OpenAPI contract no longer matches this service. Re-export it — and read "
        "the diff, because the .NET core is compiled against the old one."
    )


def test_the_contract_offers_the_core_exactly_five_things() -> None:
    """Everything the .NET core is allowed to ask an LLM for. If a new door appears, it was designed
    somewhere other than here — this test forces the change to be deliberate.

    There is **one grade door**, and the caller does not bring the standard to it.
    `/assessment/grade-by-concept` (M11.7b) takes only the learner's words and *which concept*; the
    rubric is resolved on this side of the wire. The older `/assessment/grade`, which took a full
    rubric over the wire, has been **retired** — a rubric is answer-adjacent, and the core no longer
    holds one to send. The sweep below proves the surviving door carries no answer field.

    Note there is exactly **one** door to the oracle. An earlier draft of the tutor route grew its
    own `/tutor/check` — a second way to reach the answer key. Two doors to one secret is one door
    too many. Deleted.
    """
    committed = json.loads(CONTRACT.read_text(encoding="utf-8"))

    assert set(committed["paths"]) == {
        "/health",
        "/oracle/check-claim",
        "/assessment/grade-by-concept",
        "/assessment/items",
        "/tutor/turn",
        "/tutor/turn/stream",
    }

    # The oracle still has exactly one door, and it is the one the tutor knocks on. Adding a grade
    # path did not add a way to reach the answer key.
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

    for name in ("CheckClaimRequest", "GradeByConceptRequest", "GenerateItemRequest"):
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


def test_the_generator_can_be_handed_declared_misconceptions_and_names_one_back() -> None:
    """M14.2, pinned explicitly. The core now sends the concept's declared wrong beliefs
    (`known_misconceptions`, a key-to-statement map) and the item names which one, if any, it was
    built to catch (`targets_misconception`, a key or null).

    Neither is an answer — a slug and a map of already-known wrong beliefs — which is why the sweep
    above (``test_no_schema_in_the_contract_can_carry_an_answer``) stays green without weakening it.
    """
    committed = json.loads(CONTRACT.read_text(encoding="utf-8"))

    request = committed["components"]["schemas"]["GenerateItemRequest"]["properties"]
    assert "known_misconceptions" in request, (
        "The core has no way to tell the generator what this concept's declared wrong beliefs are."
    )

    response = committed["components"]["schemas"]["ItemResponse"]["properties"]
    assert "targets_misconception" in response, (
        "The item can no longer say which declared misconception it was built to catch."
    )


def test_a_fixed_key_item_carries_a_format_in_and_a_selection_back() -> None:
    """Task 6 (M17), pinned explicitly. The core declares how an item is marked
    (`GenerateItemRequest.item_format`) and gets a passage and options back on `ItemResponse` — but
    never which option is correct. The oracle accepts a `selected_option` in place of a `claim` —
    but never both, and never neither; that is enforced by the request validator, not by this test,
    which only pins that the *fields* exist."""
    committed = json.loads(CONTRACT.read_text(encoding="utf-8"))

    request = committed["components"]["schemas"]["GenerateItemRequest"]["properties"]
    assert "item_format" in request, "The core has no way to ask for a fixed-key item."

    response = committed["components"]["schemas"]["ItemResponse"]["properties"]
    assert "passage" in response, "A reading item's passage cannot reach the learner."
    assert "options" in response, "A reading item's options cannot reach the learner."

    check_claim = committed["components"]["schemas"]["CheckClaimRequest"]["properties"]
    assert "selected_option" in check_claim, "The core has no way to submit a choice."

    # `claim` is no longer required — a fixed-key answer is a `selected_option`, not words.
    assert "claim" not in committed["components"]["schemas"]["CheckClaimRequest"]["required"]


def test_the_published_contract_has_no_property_that_could_be_a_key() -> None:
    """**The sweep, made exhaustive.** Every property name anywhere in the schema — not just the
    ones this milestone happens to be thinking about — is checked against the vocabulary of an
    answer key. This is what actually stands between a fixed-key item and a route that, someday,
    hands back the index everyone was told never crosses the wire.
    """
    schema = json.loads(CONTRACT.read_text(encoding="utf-8"))

    forbidden = {
        "correct",
        "correct_option",
        "correct_index",
        "answer",
        "key",
        "option_misconceptions",
    }

    found = [name for name in _all_property_names(schema) if name.lower() in forbidden]

    assert not found, (
        f"The published AI-plane contract advertises {found}. The contract is the thing other "
        "people build against — a key in it is a key in every client that will ever be written."
    )
