"""The front door of the AI plane."""

from fastapi.testclient import TestClient

from praxis_ai.app import app
from praxis_ai.config import Settings, get_settings


def test_health_is_honest_about_what_it_knows() -> None:
    with TestClient(app) as client:
        response = client.get("/health")

    assert response.status_code == 200

    body = response.json()

    assert body["status"] == "ok"
    assert body["service"] == "praxis-ai"


def test_health_says_whether_there_is_a_key_and_never_what_it_is() -> None:
    """A boolean. Always a boolean.

    A health endpoint that echoes configuration back is how keys end up in screenshots, in support
    tickets, and in the logs of whatever proxy sits in front of it.
    """
    app.dependency_overrides[get_settings] = lambda: Settings(
        anthropic_api_key="sk-ant-a-real-looking-secret-value"
    )

    try:
        with TestClient(app) as client:
            response = client.get("/health")
    finally:
        app.dependency_overrides.clear()

    body = response.json()

    assert body["model_configured"] is True

    # The key itself appears nowhere in the response — not in a field, not in a message, not
    # anywhere in the raw bytes.
    assert "sk-ant" not in response.text


def test_a_missing_key_is_reported_not_hidden() -> None:
    app.dependency_overrides[get_settings] = lambda: Settings(anthropic_api_key="")

    try:
        with TestClient(app) as client:
            response = client.get("/health")
    finally:
        app.dependency_overrides.clear()

    assert response.json()["model_configured"] is False


def test_every_request_is_tagged_so_its_cost_can_be_attributed() -> None:
    with TestClient(app) as client:
        response = client.get("/health")

    assert response.headers["x-request-id"]


def test_a_caller_supplied_request_id_is_honoured() -> None:
    with TestClient(app) as client:
        response = client.get("/health", headers={"x-request-id": "from-the-core"})

    # The .NET core traced it; the AI plane keeps the same thread rather than starting a new one.
    assert response.headers["x-request-id"] == "from-the-core"
