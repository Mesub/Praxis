"""Configuration for the AI plane.

**The AI plane is stateless about learners and sessions.** It keeps no memory of the last request;
everything it needs to answer arrives in the request or comes from here, which is what lets it be
scaled, restarted or thrown away without anybody losing anything.

The one durable thing it owns is the **answer-key store** (M14.1) — and it owns that precisely so
no other process ever can. A key that lived only in this process's head died with a restart, and a
banked item whose key died is a question a learner can answer and PRAXIS can never mark.
"""

from functools import lru_cache
from typing import Literal

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Read from ``ai/.env``, which is gitignored and always will be."""

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    environment: Literal["development", "test", "production"] = "development"

    # ── The key ──────────────────────────────────────────────────────────────
    #
    # A SecretStr, deliberately. Its __repr__ is "**********", so it cannot be leaked by a stray
    # print, an exception's repr, a Pydantic validation error, or a structured log that helpfully
    # serialises the whole settings object. Reading it requires saying so out loud, in code, once:
    #   settings.anthropic_api_key.get_secret_value()
    anthropic_api_key: SecretStr = Field(default=SecretStr(""))

    # ── The oracle's durable memory ──────────────────────────────────────────
    #
    # When set, generated answer keys are filed in this database (M14.1) and survive a restart.
    # A separate *database*, not a schema of `praxis`: the core's connection cannot reach it,
    # because Postgres has no cross-database queries. Empty (tests, cold dev) falls back to the
    # in-memory store, which is exactly as durable as it ever was.
    oracle_store_url: SecretStr = Field(default=SecretStr(""))

    # Opus-class for compilation, grading and synthesis; Sonnet-class for tutor turns and item
    # generation. Wired up properly by the router in M5.2.
    model_heavy: str = "claude-opus-4-8"
    model_fast: str = "claude-sonnet-5"

    log_level: str = "INFO"

    # Machine-readable JSON in production; readable lines when a human is watching.
    log_json: bool = False

    @property
    def has_api_key(self) -> bool:
        """Whether a key is configured. **Not what it is.**"""
        return bool(self.anthropic_api_key.get_secret_value().strip())

    @property
    def has_oracle_store(self) -> bool:
        """Whether the durable key store is configured. **Not where it is.**"""
        return bool(self.oracle_store_url.get_secret_value().strip())


@lru_cache
def get_settings() -> Settings:
    """A FastAPI dependency, so tests can override it rather than mutate the environment."""
    return Settings()
