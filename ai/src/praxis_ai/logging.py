"""Structured logging, with a muzzle on it.

Two things pass through this process that must **never** reach a log line, a crash report, or an
observability vendor:

1. **The Anthropic API key.** Obvious, and handled by ``SecretStr`` in the settings.
2. **The answer.** Less obvious, and far more interesting.

The whole architecture of PRAXIS rests on the tutor never having the answer in its context (C-5).
A jailbreak retrieves nothing because there is nothing there. But a *log* is a context too — and a
service that carefully withholds the answer key from the model, then writes it to stdout at INFO so
it can be grepped later, has not protected anything. It has just moved the leak somewhere with worse
access control and a longer retention period.

So redaction happens at the logging boundary, where it cannot be forgotten, rather than at each of
the hundred call sites where it could be.
"""

from __future__ import annotations

import json
import logging
import re
import sys
from contextvars import ContextVar
from typing import Any

# The current request, so every line emitted while serving it can be tied back to one learner-minute
# (M5.2 attributes cost the same way). A ContextVar rather than a global: it survives concurrency.
request_id: ContextVar[str | None] = ContextVar("request_id", default=None)

REDACTED = "[REDACTED]"

#: Things that must never appear in a log line, whatever anyone was trying to debug at the time.
_PATTERNS: tuple[re.Pattern[str], ...] = (
    # An Anthropic key, in any of the shapes it has taken.
    re.compile(r"sk-ant-[A-Za-z0-9_\-]{8,}"),
    # A bearer token in a header we were dumping "just to see".
    re.compile(r"(?i)\b(authorization|x-api-key)\b\s*[:=]\s*\S+"),
)

#: Fields whose *value* is an answer key, wherever they appear in a structured log record. Naming
#: them explicitly is the point: a field only gets logged if someone decided it was safe.
_FORBIDDEN_FIELDS = frozenset(
    {
        "answer",
        "answer_key",
        "expected",
        "solution",
        "rubric_answer",
        "api_key",
        "anthropic_api_key",
    }
)


def redact(value: Any) -> Any:
    """Scrub secrets out of anything on its way to a log."""
    if isinstance(value, str):
        for pattern in _PATTERNS:
            value = pattern.sub(REDACTED, value)
        return value

    if isinstance(value, dict):
        return {
            key: REDACTED if key.lower() in _FORBIDDEN_FIELDS else redact(item)
            for key, item in value.items()
        }

    if isinstance(value, (list, tuple)):
        return type(value)(redact(item) for item in value)

    return value


class RedactionFilter(logging.Filter):
    """The muzzle. Nothing reaches a handler without passing through here."""

    def filter(self, record: logging.LogRecord) -> bool:
        record.msg = redact(record.msg)

        if isinstance(record.args, dict):
            record.args = redact(record.args)
        elif record.args:
            record.args = tuple(redact(arg) for arg in record.args)

        for key in list(vars(record)):
            if key.lower() in _FORBIDDEN_FIELDS:
                setattr(record, key, REDACTED)

        return True


_RESERVED = frozenset(vars(logging.LogRecord("", 0, "", 0, "", None, None)))


class JsonFormatter(logging.Formatter):
    """One JSON object per line. Machines read these; humans read the console formatter."""

    def format(self, record: logging.LogRecord) -> str:
        payload: dict[str, Any] = {
            "ts": self.formatTime(record, "%Y-%m-%dT%H:%M:%S%z"),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
        }

        if (rid := request_id.get()) is not None:
            payload["request_id"] = rid

        # Whatever the caller attached via extra={...}, minus logging's own furniture.
        for key, value in vars(record).items():
            if key not in _RESERVED and key not in payload and not key.startswith("_"):
                payload[key] = value

        if record.exc_info:
            payload["exception"] = self.formatException(record.exc_info)

        return json.dumps(redact(payload), default=str)


class ConsoleFormatter(logging.Formatter):
    """For a person watching a terminal."""

    def format(self, record: logging.LogRecord) -> str:
        rid = request_id.get()
        prefix = f"[{rid[:8]}] " if rid else ""

        return f"{record.levelname:<7} {prefix}{record.name}: {record.getMessage()}"


def configure_logging(*, level: str = "INFO", json_output: bool = False) -> None:
    """Idempotent: safe to call from a lifespan that a test starts more than once."""
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JsonFormatter() if json_output else ConsoleFormatter())
    handler.addFilter(RedactionFilter())

    root = logging.getLogger()
    root.handlers.clear()
    root.addHandler(handler)
    root.setLevel(level.upper())
