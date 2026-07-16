"""The durable half of the oracle's memory (M14.1).

The in-memory store (M5.5) held generated keys only as long as the process lived — which was fine
while nothing banked items durably, and stops being fine the moment M14 does. A banked item whose
key evaporated on a restart is permanently unmarkable: a learner would spend real effort on it and
PRAXIS would have nothing to say back.

The keys live in their own *database* (``praxis_oracle``), not a schema of ``praxis`` — because
the .NET core's connection string points at ``praxis``, and Postgres does not do cross-database
queries. An SQL injection into the core still retrieves nothing, which keeps C-5 a fact about the
deployment rather than a hope about a prompt.

The shape is the ``AnswerKeyStore`` protocol and nothing more: a ``get``, an ``add``, and no
list, no search, nothing that hands out an answer to anybody but the oracle itself.
"""

from __future__ import annotations

import psycopg
from psycopg.types.json import Jsonb

from praxis_ai.oracle import AnswerKey

_SCHEMA = """
CREATE TABLE IF NOT EXISTS answer_keys (
    item_id        text PRIMARY KEY,
    concept_id     text NOT NULL,
    question       text NOT NULL,
    answer         text NOT NULL,
    misconceptions jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at     timestamptz NOT NULL DEFAULT now()
)
"""


class PostgresAnswerKeyStore:
    def __init__(self, url: str) -> None:
        self._url = url

    def ensure_schema(self) -> None:
        with psycopg.connect(self._url, autocommit=True) as conn:
            conn.execute(_SCHEMA)

    def add(self, key: AnswerKey) -> None:
        # Item ids are stem hashes: the same id is the same item, so first write wins and a
        # re-file is a no-op rather than an error.
        with psycopg.connect(self._url, autocommit=True) as conn:
            conn.execute(
                "INSERT INTO answer_keys (item_id, concept_id, question, answer, misconceptions)"
                " VALUES (%s, %s, %s, %s, %s) ON CONFLICT (item_id) DO NOTHING",
                (
                    key.item_id,
                    key.concept_id,
                    key.question,
                    key.answer,
                    Jsonb(key.misconceptions),
                ),
            )

    def get(self, item_id: str) -> AnswerKey | None:
        with psycopg.connect(self._url) as conn:
            row = conn.execute(
                "SELECT item_id, concept_id, question, answer, misconceptions"
                " FROM answer_keys WHERE item_id = %s",
                (item_id,),
            ).fetchone()

        if row is None:
            return None

        return AnswerKey(
            item_id=row[0],
            concept_id=row[1],
            question=row[2],
            answer=row[3],
            misconceptions=dict(row[4]),
        )
