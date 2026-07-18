"""``/assessment/items`` — the item-generation door, exercised through the route.

The unit tests in ``test_items.py`` prove the generator's behaviour; these prove the *wire*: what
the .NET core actually sends is what the generator receives, and what the generator decides is what
actually comes back — with no field for an answer to ride in.
"""

from __future__ import annotations

import json
from collections.abc import Iterator
from typing import Any
from unittest.mock import patch

import pytest
from fastapi.testclient import TestClient

from praxis_ai.app import app
from praxis_ai.claude import ClaudeClient
from praxis_ai.config import Settings, get_settings
from praxis_ai.oracle import InMemoryAnswerKeyStore
from praxis_ai.routes.oracle import get_store

QUESTION = "A document repeats 'jaguar' 60 times. Why does BM25 not rank it 6x above one with 10?"

THE_ANSWER = "Because term frequency saturates: the tenth occurrence adds far less than the second."


class _Model:
    def __init__(self, reply: str) -> None:
        outer = self

        class _Messages:
            async def create(self, **kwargs: Any) -> Any:
                return _Response(outer.reply)

        self.reply = reply
        self.messages = _Messages()


class _Response:
    def __init__(self, text: str) -> None:
        self.content = [type("Block", (), {"type": "text", "text": text})()]
        self.usage = type("Usage", (), {"input_tokens": 300, "output_tokens": 200})()
        self.stop_reason = "end_turn"


def _fake_claude(reply: str):
    def factory(_settings: Settings) -> ClaudeClient:
        return ClaudeClient(Settings(anthropic_api_key="sk-ant-not-real"), client=_Model(reply))

    return factory


@pytest.fixture
def client() -> Iterator[TestClient]:
    app.dependency_overrides[get_settings] = lambda: Settings(anthropic_api_key="sk-ant-not-real")
    # A lambda, not the class: handing FastAPI the class makes it read __init__ params as
    # request fields.
    app.dependency_overrides[get_store] = lambda: InMemoryAnswerKeyStore()
    yield TestClient(app)
    app.dependency_overrides.clear()


def _request(**overrides: Any) -> dict[str, Any]:
    return {
        "concept_id": "bm25",
        "concept_summary": "BM25 saturates term frequency and normalises for length.",
        "evidence_type": "apply",
        "difficulty": 0.6,
        "learner_id": "alice",
        **overrides,
    }


def test_known_misconceptions_flow_in_and_the_targeted_key_flows_back(
    client: TestClient,
) -> None:
    """M14.2 at the seam itself: the declared beliefs in the request body reach the generator, and
    the key it targets comes back as ``targets_misconception`` — the review-noted gap that the
    contract test pins the *schema* for but nothing exercised end-to-end through the route."""
    reply = json.dumps(
        {
            "question": QUESTION,
            "answer": THE_ANSWER,
            "misconceptions": {},
            "targets": "bm25-is-obsolete",
        }
    )

    with patch("praxis_ai.routes.assessment.ClaudeClient", _fake_claude(reply)):
        resp = client.post(
            "/assessment/items",
            json=_request(known_misconceptions={"bm25-is-obsolete": "I think BM25 is obsolete."}),
        )

    assert resp.status_code == 200
    body = resp.json()
    assert body["targets_misconception"] == "bm25-is-obsolete"

    # And the answer the model produced in the same breath is nowhere in what crossed back.
    assert THE_ANSWER not in resp.text


def test_a_target_outside_the_declared_set_comes_back_null(client: TestClient) -> None:
    """The airlock, observed from the wire: a slug the model invented never reaches the core."""
    reply = json.dumps(
        {
            "question": QUESTION,
            "answer": THE_ANSWER,
            "misconceptions": {},
            "targets": "made-up-slug",
        }
    )

    with patch("praxis_ai.routes.assessment.ClaudeClient", _fake_claude(reply)):
        resp = client.post(
            "/assessment/items",
            json=_request(known_misconceptions={"bm25-is-obsolete": "I think BM25 is obsolete."}),
        )

    assert resp.status_code == 200
    assert resp.json()["targets_misconception"] is None
