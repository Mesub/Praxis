"""The oracle, through the front door — and a sweep of the whole API for any way out for the answer.

The last test in this file is the one I would keep if I could keep only one. It does not test a
behaviour; it tests the **shape of the entire public surface**, and it fails the day somebody adds
the reasonable-sounding endpoint that ends this product's central claim.
"""

from __future__ import annotations

from typing import Any

from fastapi.testclient import TestClient

from praxis_ai.app import app
from praxis_ai.claude import ClaudeClient
from praxis_ai.config import Settings, get_settings
from praxis_ai.oracle import AnswerKey, InMemoryAnswerKeyStore, Oracle
from praxis_ai.routes.oracle import get_store

THE_ANSWER = "BM25 saturates term frequency, so a tenth occurrence adds far less than the second."

KEY = AnswerKey(
    item_id="item-1",
    concept_id="bm25",
    question="Why does BM25 not reward 100 occurrences ten times as much as 10?",
    answer=THE_ANSWER,
    misconceptions={"thinks-tf-is-linear": "Believes term frequency contributes linearly."},
)


class _Model:
    def __init__(self, reply: str) -> None:
        outer = self
        self.reply = reply

        class _Messages:
            async def create(self, **kwargs: Any) -> Any:
                return _Response(outer.reply)

        self.messages = _Messages()


class _Response:
    def __init__(self, text: str) -> None:
        self.content = [type("Block", (), {"type": "text", "text": text})()]
        self.usage = type("Usage", (), {"input_tokens": 400, "output_tokens": 20})()
        self.stop_reason = "end_turn"


def _client(reply: str, store: InMemoryAnswerKeyStore | None = None) -> TestClient:
    settings = Settings(anthropic_api_key="sk-ant-not-real")

    if store is None:
        store = InMemoryAnswerKeyStore({KEY.item_id: KEY})

    # Patch the one place the oracle is built, so the route runs for real against a fake model.
    import praxis_ai.routes.oracle as route

    route.Oracle = lambda _client, _store: Oracle(  # type: ignore[assignment]
        ClaudeClient(settings, client=_Model(reply)), store
    )

    app.dependency_overrides[get_settings] = lambda: settings
    app.dependency_overrides[get_store] = lambda: store

    return TestClient(app)


def _reset() -> None:
    import praxis_ai.routes.oracle as route

    route.Oracle = Oracle   # type: ignore[assignment]
    app.dependency_overrides.clear()


def _post(reply: str, claim: str = "I have no idea", item_id: str = "item-1") -> Any:
    try:
        with _client(reply) as client:
            return client.post(
                "/oracle/check-claim",
                json={
                    "item_id": item_id,
                    "claim": claim,
                    "learner_id": "alice",
                    "concept_id": "bm25",
                },
            )
    finally:
        _reset()


def test_the_tutor_gets_a_verdict_and_nothing_else() -> None:
    response = _post('{"outcome": "wrong", "misconception": "thinks-tf-is-linear"}')

    assert response.status_code == 200

    body = response.json()

    assert body["outcome"] == "wrong"
    assert body["misconception"] == "thinks-tf-is-linear"

    # The whole body, as bytes. The answer is not in it.
    assert THE_ANSWER not in response.text
    assert "saturat" not in response.text


def test_the_answer_does_not_come_back_even_when_the_model_shouts_it() -> None:
    response = _post(
        f'{{"outcome": "wrong", "misconception": null, "correct_answer": "{THE_ANSWER}"}}'
    )

    assert response.status_code == 200
    assert THE_ANSWER not in response.text


def test_an_unmarkable_item_is_a_404_not_a_verdict() -> None:
    response = _post('{"outcome": "correct", "misconception": null}', item_id="never-written")

    assert response.status_code == 404


def test_an_unclear_reply_is_a_422_and_never_a_wrong() -> None:
    """The tutor will say "I could not check that". It will **not** tell a learner they were wrong
    because our parser failed — they may well have been right."""
    response = _post("I think the learner did quite well, honestly.")

    assert response.status_code == 422

    # And crucially: there is no verdict in the body at all. Not a wrong, not a partial, not a
    # "we'll call it wrong for now". The tutor has nothing to act on, which is correct — because
    # the learner may well have been right, and the failure was ours.
    assert "outcome" not in response.json()


# ── Fixed-key marking (Task 6, M17) ─────────────────────────────────────────

FIXED_KEY = AnswerKey(
    item_id="item-mc-1",
    concept_id="c1",
    question="q",
    answer="the second",
    misconceptions={"keyword-match-wins": "Believes more repeats always wins."},
    correct_option=1,
    option_misconceptions={0: "keyword-match-wins"},
)


def test_a_selection_marks_without_a_claim() -> None:
    """Two integers, no model. The reply text is never read, because a fixed-key item never calls
    the model at all."""
    store = InMemoryAnswerKeyStore({FIXED_KEY.item_id: FIXED_KEY})

    try:
        with _client("should never be read", store=store) as client:
            response = client.post(
                "/oracle/check-claim",
                json={
                    "item_id": FIXED_KEY.item_id,
                    "selected_option": 0,
                    "learner_id": "l1",
                    "concept_id": "c1",
                },
            )
    finally:
        _reset()

    assert response.status_code == 200
    assert response.json() == {
        "outcome": "wrong",
        "misconception": "keyword-match-wins",
        "usd": 0.0,
    }


def test_neither_a_claim_nor_a_selection_is_a_422() -> None:
    """A caller that submits nothing is a caller that does not know what it is submitting."""
    try:
        with _client("unused") as client:
            response = client.post(
                "/oracle/check-claim",
                json={"item_id": "item-1", "learner_id": "l1", "concept_id": "c1"},
            )
    finally:
        _reset()

    assert response.status_code == 422


def test_both_a_claim_and_a_selection_is_a_422() -> None:
    """``Oracle._mark_fixed_key`` does not inspect ``claim`` — if both a claim and a selection
    arrived, it would silently ignore the claim and mark from the selection alone. The request
    validator is the only thing that actually enforces one-or-the-other."""
    try:
        with _client("unused") as client:
            response = client.post(
                "/oracle/check-claim",
                json={
                    "item_id": "item-1",
                    "claim": "I think it's the second one",
                    "selected_option": 0,
                    "learner_id": "l1",
                    "concept_id": "c1",
                },
            )
    finally:
        _reset()

    assert response.status_code == 422


# ── The sweep ────────────────────────────────────────────────────────────────


def test_no_route_in_the_entire_api_can_return_an_answer() -> None:
    """**The test I would keep if I could keep only one.**

    Someone will one day be asked for "show the answer after the third failed attempt", or a debug
    endpoint, or an admin view for the content team. Each is reasonable. Each is the end of the
    claim that the tutor cannot give you the answer, because the tutor is a program with an HTTP
    client and it can call any route this service exposes.

    So: there is no route whose name suggests it hands out an answer, and no response model with a
    field that could carry one.
    """
    schema = app.openapi()

    forbidden = ("answer", "key", "solution", "reveal")

    for path in schema["paths"]:
        assert not any(word in path.lower() for word in forbidden), (
            f"{path} looks like it hands out an answer. If it does, the tutor can call it, and "
            "PRAXIS's central claim is now marketing."
        )

    for name, model in schema.get("components", {}).get("schemas", {}).items():
        for field in model.get("properties", {}):
            assert field.lower() not in {"answer", "correct_answer", "solution", "answer_key"}, (
                f"{name}.{field} can carry an answer out of this service."
            )
