"""``/assessment/grade-by-concept`` — the teach-back grade path.

The caller sends the learner's words and *which concept*, and nothing else. The rubric is resolved
here, on the AI plane, and graded against — it never crosses the wire in either direction. If no
rubric is authored for the concept, PRAXIS declines rather than inventing a standard.
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
from praxis_ai.grading import GradeOutcome
from praxis_ai.routes.assessment import get_rubric_store
from praxis_ai.rubric_store import InMemoryRubricStore, default_rubric_store

GOOD = (
    "An inverted index maps each term to the posting list of documents that contain it. It costs "
    "write time to build, but a query then reads one short list instead of scanning every document."
)


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
        self.usage = type("Usage", (), {"input_tokens": 800, "output_tokens": 60})()
        self.stop_reason = "end_turn"


def _fake_claude(reply: str):
    def factory(_settings: Settings) -> ClaudeClient:
        return ClaudeClient(Settings(anthropic_api_key="sk-ant-not-real"), client=_Model(reply))

    return factory


@pytest.fixture
def client() -> Iterator[TestClient]:
    app.dependency_overrides[get_settings] = lambda: Settings(anthropic_api_key="sk-ant-not-real")
    app.dependency_overrides[get_rubric_store] = default_rubric_store
    yield TestClient(app)
    app.dependency_overrides.clear()


def _bands(mechanism: int, tradeoff: int) -> str:
    return json.dumps({"bands": {"mechanism": mechanism, "tradeoff": tradeoff}})


def test_grades_a_concept_with_the_rubric_resolved_here_not_sent(client: TestClient) -> None:
    with patch("praxis_ai.routes.assessment.ClaudeClient", _fake_claude(_bands(4, 4))):
        resp = client.post(
            "/assessment/grade-by-concept",
            json={"response": GOOD, "learner_id": "alice", "concept_id": "inverted-index"},
        )

    assert resp.status_code == 200
    body = resp.json()
    assert body["outcome"] == GradeOutcome.PASSED.value
    assert body["score"] is not None
    # The rubric was never in the request and is not echoed back — it stays this side of the wire.
    assert "rubric" not in body


def test_the_request_carries_no_rubric_field(client: TestClient) -> None:
    # A caller cannot smuggle its own standard in: extra fields are ignored, and the stored rubric
    # is the one that grades.
    with patch("praxis_ai.routes.assessment.ClaudeClient", _fake_claude(_bands(4, 4))):
        resp = client.post(
            "/assessment/grade-by-concept",
            json={
                "response": GOOD,
                "learner_id": "alice",
                "concept_id": "inverted-index",
                "rubric": {"id": "attacker-supplied", "pass_score": 0.0},
            },
        )
    assert resp.status_code == 200


def test_declines_when_no_rubric_is_authored_for_the_concept(client: TestClient) -> None:
    # Override to an empty store so even the seeded concept is absent. (A lambda, not the class:
    # handing FastAPI the class makes it read __init__ params as request fields.)
    app.dependency_overrides[get_rubric_store] = lambda: InMemoryRubricStore()
    with patch("praxis_ai.routes.assessment.ClaudeClient", _fake_claude(_bands(4, 4))):
        resp = client.post(
            "/assessment/grade-by-concept",
            json={"response": GOOD, "learner_id": "alice", "concept_id": "unknown-concept"},
        )
    assert resp.status_code == 422
    assert "rubric" in resp.json()["detail"].lower()
