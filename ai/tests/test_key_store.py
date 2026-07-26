"""The durable answer-key store (M14.1). The key survives what memory does not: a restart.

These tests need the dev Postgres on port 5455 — the same container the .NET tests already
require. They create the ``praxis_oracle`` database idempotently, exactly as ``dev.ps1`` does.
"""

from __future__ import annotations

import psycopg
import pytest

from praxis_ai.key_store import PostgresAnswerKeyStore
from praxis_ai.oracle import AnswerKey

MAINTENANCE = "postgresql://praxis:praxis_dev_only@localhost:5455/praxis"
ORACLE_URL = "postgresql://praxis:praxis_dev_only@localhost:5455/praxis_oracle"


@pytest.fixture(scope="session")
def oracle_url() -> str:
    """The oracle database, created idempotently — the same dependency the .NET tests have."""
    with psycopg.connect(MAINTENANCE, autocommit=True) as conn:
        try:
            conn.execute("CREATE DATABASE praxis_oracle")
        except psycopg.errors.DuplicateDatabase:
            pass

    store = PostgresAnswerKeyStore(ORACLE_URL)
    store.ensure_schema()

    return ORACLE_URL


@pytest.fixture()
def store(oracle_url: str) -> PostgresAnswerKeyStore:
    with psycopg.connect(oracle_url, autocommit=True) as conn:
        conn.execute("DELETE FROM answer_keys")

    return PostgresAnswerKeyStore(oracle_url)


KEY = AnswerKey(
    item_id="item-abc123",
    concept_id="bm25",
    question="Why does BM25 saturate term frequency?",
    answer="Marking standard: diminishing returns via k1; length normalisation via b.",
    misconceptions={
        "thinks-tf-is-linear": "I think a term twice as frequent scores twice as much."
    },
)


def test_a_key_round_trips(store: PostgresAnswerKeyStore) -> None:
    store.add(KEY)

    assert store.get("item-abc123") == KEY


def test_the_key_survives_a_restart(store: PostgresAnswerKeyStore, oracle_url: str) -> None:
    """A new store instance over the same database is a restarted process, as far as keys care."""
    store.add(KEY)

    reborn = PostgresAnswerKeyStore(oracle_url)

    assert reborn.get("item-abc123") == KEY


def test_an_unknown_item_is_none_not_a_guess(store: PostgresAnswerKeyStore) -> None:
    assert store.get("item-never-made") is None


def test_the_same_item_id_filed_twice_keeps_the_first_key(store: PostgresAnswerKeyStore) -> None:
    """Item ids are stem hashes; a re-file is the same item. First write wins, no error."""
    store.add(KEY)
    store.add(AnswerKey("item-abc123", "bm25", "q", "different", {}))

    got = store.get("item-abc123")

    assert got is not None
    assert got.answer == KEY.answer


def test_the_store_has_the_oracle_shape_and_nothing_more(store: PostgresAnswerKeyStore) -> None:
    """A ``get`` and an ``add``. No list, no search, nothing that hands out answers in bulk."""
    public = {name for name in dir(store) if not name.startswith("_")}

    assert public == {"add", "get", "ensure_schema"}


def test_a_fixed_key_survives_a_round_trip(store):
    store.add(
        AnswerKey(
            item_id="item-mcq",
            concept_id="c1",
            question="q",
            answer="the second one",
            misconceptions={"keyword-match-wins": "..."},
            correct_option=1,
            option_misconceptions={0: "keyword-match-wins"},
        )
    )

    got = store.get("item-mcq")

    assert got.correct_option == 1
    assert got.option_misconceptions == {0: "keyword-match-wins"}


def test_a_prose_key_still_round_trips_with_no_index(store):
    store.add(
        AnswerKey(
            item_id="item-prose", concept_id="c1", question="q", answer="a", misconceptions={}
        )
    )

    got = store.get("item-prose")

    assert got.correct_option is None
    assert got.option_misconceptions == {}
