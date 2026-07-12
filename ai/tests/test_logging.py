"""**The muzzle.**

PRAXIS's central architectural claim is that the tutor cannot give the answer, because the answer is
never in its context (C-5). A jailbreak retrieves nothing because there is nothing there.

A log is a context too.

A service that carefully withholds the answer key from the model and then writes it to stdout at
INFO has not protected anything — it has moved the leak somewhere with worse access control and a
much longer retention period. So these tests treat a leaked answer in a log line as exactly what it
is: the same failure, wearing a different hat.
"""

import json
import logging

from praxis_ai.config import Settings
from praxis_ai.logging import REDACTED, JsonFormatter, RedactionFilter, redact


def _record(msg: str, **extra: object) -> logging.LogRecord:
    record = logging.LogRecord("praxis.test", logging.INFO, __file__, 1, msg, None, None)

    for key, value in extra.items():
        setattr(record, key, value)

    return record


# ── The answer ───────────────────────────────────────────────────────────────


def test_an_answer_key_never_survives_a_log_line() -> None:
    """The one that matters.

    The oracle (M5.3) holds the answer. If someone logs the payload it was called with — to debug a
    grading disagreement, entirely in good faith — the answer key ends up in a log aggregator that a
    great many more people can read than can read the oracle's store.
    """
    formatted = JsonFormatter().format(
        _record("check_claim", concept="bm25", answer="term frequency saturation")
    )

    payload = json.loads(formatted)

    assert payload["answer"] == REDACTED
    assert "saturation" not in formatted

    # And the rest of the line still says something useful. Redaction that destroys the log's
    # purpose gets turned off by the first person who needs to debug something.
    assert payload["concept"] == "bm25"
    assert payload["message"] == "check_claim"


def test_the_answer_is_redacted_however_deeply_it_is_buried() -> None:
    scrubbed = redact({"item": {"prompt": "why?", "grading": {"expected": "because BM25"}}})

    assert scrubbed["item"]["grading"]["expected"] == REDACTED
    assert scrubbed["item"]["prompt"] == "why?"


# ── The key ──────────────────────────────────────────────────────────────────


def test_an_api_key_never_survives_a_log_line() -> None:
    record = _record("calling anthropic with sk-ant-abc123def456ghi789")

    RedactionFilter().filter(record)

    assert "sk-ant-abc123def456ghi789" not in record.getMessage()
    assert REDACTED in record.getMessage()


def test_a_dumped_auth_header_is_scrubbed() -> None:
    record = _record("headers: x-api-key: sk-ant-oops, content-type: application/json")

    RedactionFilter().filter(record)

    assert "sk-ant-oops" not in record.getMessage()


def test_the_key_is_not_leaked_by_printing_the_settings() -> None:
    """The commonest accident: a structured log that helpfully serialises the whole config."""
    settings = Settings(anthropic_api_key="sk-ant-a-real-looking-secret-value")

    assert "sk-ant" not in repr(settings)
    assert "sk-ant" not in str(settings)
    assert "sk-ant" not in str(settings.model_dump())

    # Reading it has to be said out loud, in code, on purpose.
    assert settings.anthropic_api_key.get_secret_value() == "sk-ant-a-real-looking-secret-value"
    assert settings.has_api_key is True


# ── And it still works as a log ──────────────────────────────────────────────


def test_a_normal_line_passes_through_untouched() -> None:
    payload = json.loads(JsonFormatter().format(_record("plan compiled", concepts=30)))

    assert payload["message"] == "plan compiled"
    assert payload["concepts"] == 30
    assert payload["level"] == "INFO"
