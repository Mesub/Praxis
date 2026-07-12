"""Configuration for the AI plane.

**The AI plane is stateless.** It owns no database, no session, and no memory of the last request.
Everything it needs to answer arrives in the request or comes from here. That is what lets it be
scaled, restarted or thrown away without anybody losing anything — and it is why the *answer key*
lives behind the oracle (M5.3) rather than in this process's head.
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


@lru_cache
def get_settings() -> Settings:
    """A FastAPI dependency, so tests can override it rather than mutate the environment."""
    return Settings()
