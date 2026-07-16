# Item Bank Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire item generation into a durable bank so the adaptive diagnostic runs end to end — with answer keys that survive an AI-plane restart.

**Architecture:** Three seams. (1) The AI plane's answer-key store gains a Postgres backing in a **separate database** (`praxis_oracle`) the .NET core has no connection to — cross-database queries are impossible in Postgres, so an injection into the core's connection still retrieves nothing. (2) A new `EnsureConceptPool` command slice in Assessment calls the existing `IAiPlane.GenerateItemAsync` and persists `BankedItem` rows (stem only — the items table has no answer column and never will). (3) The web diagnostic calls the pool endpoint before asking for items; if it doesn't, nothing breaks — `NextItem` keeps its honest refusal.

**Tech Stack:** .NET 10 (vertical slices, `Result<T>`, EF Core + Npgsql), Python 3.14 + FastAPI + psycopg3, Next.js + TypeScript, PostgreSQL 17 on port **5455**.

**Spec:** `docs/superpowers/specs/2026-07-16-item-bank-wiring-design.md`

## Global Constraints

- Postgres is `localhost:5455`, user `praxis`, password `praxis_dev_only` (per project `CLAUDE.md`). The oracle store lives in a **new database** `praxis_oracle` on the same instance.
- Slice pattern: one use case = one file = `Command` + `Validator` + `Handler` + `Response`. No mediator; handlers injected `[FromServices]`. Controllers call `.ToApiResponse()`.
- Validators must live on the type the wire binds (the M6.5b lesson) — a validator on an inner `Command` a controller constructs by hand never fires.
- One handler = one use case = one `SaveChangesAsync`.
- **No answer, rubric, or answer-adjacent field may be added to any .NET type, any `assessment` table, or any AI-plane response.** `SchemaTests` and both answer-field sweeps must stay green without being weakened.
- Every task: `scripts/verify.ps1` passes (all stages), PLAN.md ticked with a one-line note, committed with the milestone id (M14.x).
- Contract changes (OpenAPI) are **deliberate acts**: regenerate `contracts/ai-plane.openapi.json` via `ai/scripts/export_contract.py` and update both guard tests (`ai/tests/test_contract.py`, `tests/Praxis.Infrastructure.Tests/Ai/AiPlaneContractTests.cs`) in the same commit.
- Commands: .NET `dotnet build/test Praxis.slnx`; AI (from `ai/`): `uv run pytest -q`, `uv run ruff check .`; web (from `web/`): `npm run test`.

---

### Task 1 (M14.1): Postgres-backed answer-key store on the AI plane

**Files:**
- Modify: `ai/pyproject.toml` (add `psycopg[binary]>=3.2` to `dependencies`)
- Modify: `ai/src/praxis_ai/config.py` (new setting; honest docstring update)
- Create: `ai/src/praxis_ai/key_store.py`
- Modify: `ai/src/praxis_ai/routes/oracle.py:33-39` (store wiring)
- Create: `scripts/db/init/02-oracle-database.sql`
- Modify: `scripts/dev.ps1` (idempotent `CREATE DATABASE praxis_oracle` for existing volumes)
- Test: `ai/tests/test_key_store.py`
- Modify: `PLAN.md` (add the `## M14 — The item bank, wired` section with all 7 checkboxes; tick M14.1 when done)

**Interfaces:**
- Consumes: `AnswerKey`, `AnswerKeyStore` protocol (`ai/src/praxis_ai/oracle.py:74-98`) — `get(item_id) -> AnswerKey | None`; the in-memory store's `add(key)`.
- Produces: `PostgresAnswerKeyStore(url)` with `ensure_schema()`, `add(key: AnswerKey) -> None`, `get(item_id: str) -> AnswerKey | None`. Setting `Settings.oracle_store_url: SecretStr` and `Settings.has_oracle_store: bool`. `get_store()` in `routes/oracle.py` now returns the Postgres store when configured.

- [ ] **Step 1: Write the failing tests** (`ai/tests/test_key_store.py`)

```python
"""The durable answer-key store. The key survives what memory does not: a restart."""

import psycopg
import pytest

from praxis_ai.key_store import PostgresAnswerKeyStore
from praxis_ai.oracle import AnswerKey

MAINTENANCE = "postgresql://praxis:praxis_dev_only@localhost:5455/praxis"
ORACLE_URL = "postgresql://praxis:praxis_dev_only@localhost:5455/praxis_oracle"


@pytest.fixture(scope="session")
def oracle_url() -> str:
    """The oracle database, created idempotently — same dependency the .NET tests already have."""
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
    s = PostgresAnswerKeyStore(oracle_url)
    with psycopg.connect(oracle_url, autocommit=True) as conn:
        conn.execute("DELETE FROM answer_keys")
    return s


KEY = AnswerKey(
    item_id="item-abc123",
    concept_id="bm25",
    question="Why does BM25 saturate term frequency?",
    answer="Marking standard: diminishing returns via k1; length normalisation via b.",
    misconceptions={"thinks-tf-is-linear": "I think a term twice as frequent scores twice as much."},
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
    assert got is not None and got.answer == KEY.answer


def test_the_store_has_the_oracle_shape_and_nothing_more(store: PostgresAnswerKeyStore) -> None:
    """A `get` and an `add`. No list, no search, nothing that hands out answers in bulk."""
    public = {name for name in dir(store) if not name.startswith("_")}
    assert public == {"add", "get", "ensure_schema"}
```

- [ ] **Step 2: Run to verify failure** — `cd ai; uv run pytest tests/test_key_store.py -q` → FAIL (`ModuleNotFoundError: praxis_ai.key_store`). (Postgres must be up: `docker compose up -d` at repo root first — same requirement the .NET tests already have.)

- [ ] **Step 3: Implement.** Add the dependency (`uv add "psycopg[binary]>=3.2"` from `ai/`), then:

`ai/src/praxis_ai/key_store.py`:

```python
"""The durable half of the oracle's memory.

The in-memory store (M5.5) held generated keys only as long as the process lived — which was fine
while nothing banked items durably, and stops being fine the moment M14 does. A banked item whose
key evaporated on a restart is permanently unmarkable: a learner would spend real effort on it and
PRAXIS would have nothing to say back.

The keys live in their own database (`praxis_oracle`), not a schema of `praxis` — because the .NET
core's connection string points at `praxis`, and Postgres does not do cross-database queries. An
SQL injection into the core still retrieves nothing, which keeps C-5 a fact about the deployment.

The shape is the ``AnswerKeyStore`` protocol and nothing more: a ``get``, an ``add``, and no list,
no search, nothing that hands out an answer to anybody but the oracle itself.
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
                (key.item_id, key.concept_id, key.question, key.answer, Jsonb(key.misconceptions)),
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
            item_id=row[0], concept_id=row[1], question=row[2], answer=row[3],
            misconceptions=dict(row[4]),
        )
```

`ai/src/praxis_ai/config.py` — add below `anthropic_api_key`:

```python
    # ── The oracle's durable memory ──────────────────────────────────────────
    #
    # When set, generated answer keys are filed in this database (M14.1) and survive a restart.
    # A separate *database*, not a schema of `praxis`: the core's connection cannot reach it.
    # Empty (tests, cold dev) falls back to the in-memory store.
    oracle_store_url: SecretStr = Field(default=SecretStr(""))

    @property
    def has_oracle_store(self) -> bool:
        return bool(self.oracle_store_url.get_secret_value().strip())
```

Also amend the module docstring's "It owns no database" sentence to: stateless about *learners and sessions*; the one durable thing it owns is the answer-key store, which exists precisely so no other process ever can.

`ai/src/praxis_ai/routes/oracle.py` — replace lines 33-39 (`_store = InMemoryAnswerKeyStore()` and `get_store`) with:

```python
#: Populated by the item generator (M5.5). Durable when `oracle_store_url` is configured (M14.1);
#: in-memory otherwise, which is what tests and a cold dev checkout get.
_store: AnswerKeyStore | None = None


def get_store(settings: Annotated[Settings, Depends(get_settings)]) -> AnswerKeyStore:
    global _store

    if _store is None:
        if settings.has_oracle_store:
            durable = PostgresAnswerKeyStore(settings.oracle_store_url.get_secret_value())
            durable.ensure_schema()
            _store = durable
        else:
            _store = InMemoryAnswerKeyStore()

    return _store
```

(Add the imports: `from typing import Annotated`, `from fastapi import Depends`, `from praxis_ai.config import Settings, get_settings`, `from praxis_ai.key_store import PostgresAnswerKeyStore`. Check existing imports first — several are already there. If any existing test calls `get_store()` with no args, update it to pass `Settings()` or override the dependency.)

`scripts/db/init/02-oracle-database.sql` (fresh volumes):

```sql
-- The oracle's database. Separate from `praxis` on purpose: the .NET core's connection string
-- points at `praxis`, and Postgres has no cross-database queries — so the answer keys filed here
-- are unreachable from the core even in principle (C-5, M14.1).
CREATE DATABASE praxis_oracle;
```

`scripts/dev.ps1` — after the existing Postgres health wait, add the idempotent guard for volumes that predate this file:

```powershell
# The oracle database (M14.1). Init scripts only run on a fresh volume, so ensure it here too.
$oracleExists = docker exec praxis-postgres psql -U praxis -d praxis -tAc "SELECT 1 FROM pg_database WHERE datname = 'praxis_oracle'"
if ($oracleExists -ne "1") {
    docker exec praxis-postgres psql -U praxis -d praxis -c "CREATE DATABASE praxis_oracle" | Out-Null
}
```

Also add to `ai/.env` (gitignored — do not commit): `ORACLE_STORE_URL=postgresql://praxis:praxis_dev_only@localhost:5455/praxis_oracle`.

- [ ] **Step 4: Run tests** — `cd ai; uv run pytest -q` → all pass (new file green, nothing else broken). `uv run ruff check .` → clean.
- [ ] **Step 5: Full gate** — `.\scripts\verify.ps1` → every stage passes.
- [ ] **Step 6: Tick + commit**

```bash
git add -A
git commit -m "M14.1 - the durable answer-key store: a banked item is markable after a restart"
```

---

### Task 2 (M14.2): The generator learns the declared misconceptions — AI-plane half

**Files:**
- Modify: `ai/src/praxis_ai/items.py` (new `known_misconceptions` input, `targets_misconception` output)
- Modify: `ai/src/praxis_ai/routes/assessment.py:124-184` (request/response models + pass-through)
- Modify: `contracts/ai-plane.openapi.json` (regenerated, never hand-edited)
- Modify: `ai/tests/test_contract.py` (the deliberate guard update)
- Test: `ai/tests/test_items.py` (extend the existing file)

**Interfaces:**
- Consumes: `ItemGenerator.generate(...)` (`items.py:122`), `Item` dataclass, `_parse` (`items.py:265`), `GenerateItemRequest`/`ItemResponse` Pydantic models (`routes/assessment.py:124-150`).
- Produces: `generate(..., known_misconceptions: dict[str, str] | None = None)`; `Item.targets_misconception: str | None`; wire fields `known_misconceptions: dict[str, str] = {}` (request) and `targets_misconception: str | None` (response). Task 3's .NET records mirror these names in PascalCase.

- [ ] **Step 1: Write the failing tests** — add to `ai/tests/test_items.py` (follow that file's existing fake-client pattern; read it first and reuse its fixtures):

```python
async def test_the_generator_is_handed_the_declared_wrong_beliefs_and_targets_one() -> None:
    """The reply names a declared key; it survives to the item. Free-text slugs never do."""
    client = fake_client_replying(
        '{"question": "Q?", "answer": "A.", "misconceptions": {}, "targets": "bm25-is-obsolete"}'
    )
    generator = ItemGenerator(client, InMemoryAnswerKeyStore())
    item = await generator.generate(
        concept_id="bm25", concept_summary="BM25.", evidence_type=EvidenceType.APPLY,
        difficulty=0.5, attribution=ATTRIBUTION,
        known_misconceptions={"bm25-is-obsolete": "I think BM25 is obsolete."},
    )
    assert item.targets_misconception == "bm25-is-obsolete"


async def test_an_undeclared_target_is_dropped_never_invented() -> None:
    client = fake_client_replying(
        '{"question": "Q?", "answer": "A.", "misconceptions": {}, "targets": "made-up-slug"}'
    )
    generator = ItemGenerator(client, InMemoryAnswerKeyStore())
    item = await generator.generate(
        concept_id="bm25", concept_summary="BM25.", evidence_type=EvidenceType.APPLY,
        difficulty=0.5, attribution=ATTRIBUTION,
        known_misconceptions={"bm25-is-obsolete": "I think BM25 is obsolete."},
    )
    assert item.targets_misconception is None


async def test_no_declared_beliefs_means_no_target_and_no_prompt_section() -> None:
    client = fake_client_replying('{"question": "Q?", "answer": "A.", "misconceptions": {}}')
    generator = ItemGenerator(client, InMemoryAnswerKeyStore())
    item = await generator.generate(
        concept_id="tokenisation", concept_summary="Tokens.", evidence_type=EvidenceType.APPLY,
        difficulty=0.3, attribution=ATTRIBUTION,
    )
    assert item.targets_misconception is None
    assert "KNOWN WRONG BELIEFS" not in client.last_prompt  # capture the prompt the way the
    # existing tests in this file do — reuse their fake-client, do not invent a second one
```

- [ ] **Step 2: Run to verify failure** — `cd ai; uv run pytest tests/test_items.py -q` → FAIL (`unexpected keyword argument 'known_misconceptions'`).

- [ ] **Step 3: Implement** in `items.py`:
  - `Item` gains `targets_misconception: str | None = field(compare=False, default=None)`.
  - `generate()` and `_draft()` gain `known_misconceptions: dict[str, str] | None = None`, threaded to `_prompt`.
  - `_prompt` appends, when non-empty:

```python
    beliefs = "\n".join(f"- {k}: {v}" for k, v in (known or {}).items())
    targeting = (
        f"\n\nKNOWN WRONG BELIEFS for this concept (key: the belief):\n{beliefs}\n"
        "If your item is built to catch one of these, name its key in `targets`. "
        "Only these keys are valid; anything else will be discarded."
        if known
        else ""
    )
```

  - `_SYSTEM` reply shape becomes `{"question": "...", "answer": "...", "misconceptions": {...}, "targets": <key or null>}`.
  - `_parse` gains the airlock rule (same discipline as the oracle's `_parse`): `targets` must be a declared key or it becomes `None` — logged as a warning, never passed through. `_parse(text, known_keys: frozenset[str])`.
  - The answer key's `misconceptions` dict is the union of the generator's own map and the declared statements for any targeted key (so the oracle can recognise the belief at marking time).
  - In `routes/assessment.py`: `GenerateItemRequest` gains `known_misconceptions: dict[str, str] = Field(default_factory=dict)`; `ItemResponse` gains `targets_misconception: str | None = None`; the route passes both through.

- [ ] **Step 4: Run tests** — `uv run pytest -q` → the two new tests pass; the existing item tests still pass (defaults preserve old behavior).
- [ ] **Step 5: Regenerate the contract, update the guard** — `cd ai; uv run python scripts/export_contract.py` (writes `contracts/ai-plane.openapi.json`). Run `uv run pytest tests/test_contract.py -q`; it will fail on the changed surface — update its pinned expectations **explicitly** for the two new fields, and confirm the answer-field sweep still passes (the new fields are a key and a map of wrong beliefs — neither is an answer).
- [ ] **Step 6: Full gate** — `.\scripts\verify.ps1`. Note: the **.NET** contract guard (`AiPlaneContractTests`) may also pin this JSON — if it goes red, that is Task 3's deliberate update; do Task 3's contract-record step in this same commit if the gate demands it (the two halves of one wire change belong together).
- [ ] **Step 7: Tick + commit** — `git commit -m "M14.2 - the generator is handed the declared wrong beliefs, and names which one an item hunts"`

---

### Task 3 (M14.3): The .NET side of the wire, and the item remembers its oracle name

**Files:**
- Modify: `src/Praxis.Infrastructure/Ai/AiPlaneContract.cs:179-203` (both records)
- Modify: `src/Praxis.Infrastructure/Ai/AiPlaneClient.cs:45` (pass-through only; check serialization casing matches the existing fields)
- Modify: `src/Modules/Praxis.Modules.Assessment/Domain/BankedItem.cs` (new `OracleId`)
- Modify: `src/Modules/Praxis.Modules.Assessment/Persistence/AssessmentDbContext.cs:48-72` (map it)
- Create: migration `ItemOracleId` in `src/Modules/Praxis.Modules.Assessment/Persistence/Migrations/`
- Modify: `src/Modules/Praxis.Modules.Assessment/Features/Diagnostics/SubmitAnswer.cs:105-111` (send the oracle id)
- Modify: `tests/Praxis.Infrastructure.Tests/Ai/AiPlaneContractTests.cs` (deliberate)
- Modify: fakes — `tests/Praxis.Api.Tests/SessionsApiTests.cs:576` (`LeakingAiPlane`), `tests/Praxis.Api.Tests/EvidenceCeilingE2ETests.cs:182`, `tests/Praxis.Modules.Assessment.Tests/FakeOracle.cs`, `tests/Praxis.Modules.Memory.Tests/Harness.cs:19`
- Test: extend `tests/Praxis.Modules.Assessment.Tests` (SubmitAnswer already has coverage — find its test file and add the oracle-id case)

**Interfaces:**
- Consumes: Task 2's wire fields.
- Produces:

```csharp
public sealed record GenerateItemRequest(
    string ConceptId,
    string ConceptSummary,
    EvidenceType EvidenceType,
    double Difficulty,
    string LearnerId,
    IReadOnlyList<string> AlreadySeen,
    /// The concept's declared wrong beliefs, key → first-person statement. Keys, never answers:
    /// naming how a learner is likely wrong is the opposite of handing them what is right (C-5).
    IReadOnlyDictionary<string, string> KnownMisconceptions);

public sealed record ItemResponse(
    string Id, string ConceptId, EvidenceType EvidenceType, string Stem, double Difficulty,
    string Fingerprint,
    /// The declared key this item's distractors hunt, or null. Validated on the far side against
    /// the declared set — never free text.
    string? TargetsMisconception);
```

  `BankedItem.OracleId: string?` — the identifier the oracle filed this item's key under; `BankedItem.From(..., string? oracleId = null)` (new optional last parameter). `SubmitAnswer` sends `item.OracleId ?? item.Id.ToString()` as `CheckClaimRequest.ItemId`. Task 5 consumes all of this.

- [ ] **Step 1: Write the failing test** — in the Assessment tests, next to the existing SubmitAnswer tests (grep `SubmitAnswer` under `tests/` to find the file; follow its fixture pattern):

```csharp
[Fact]
public async Task The_claim_is_checked_under_the_name_the_oracle_knows_the_item_by()
{
    // A banked item generated by the AI plane carries the oracle's id for it ("item-<hash>").
    // The Guid is ours; the oracle never heard of it. Marking must ask by the oracle's name.
    var item = BankedItem.From(
        Guid.CreateVersion7(), concept, EvidenceType.Apply, "stem", "fp-1",
        difficulty: 0.0, oracleId: "item-abcdef1234567890");
    // ...seed item, submit an answer through the handler with a capturing fake IAiPlane...
    Assert.Equal("item-abcdef1234567890", capturedCheckClaim.ItemId);
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test Praxis.slnx --nologo -v quiet` (or the single test with `--filter`) → FAIL: `From` has no `oracleId` parameter.
- [ ] **Step 3: Implement**
  - `BankedItem`: `public string? OracleId { get; private set; }` with the doc line *"the name the oracle filed this item's key under; null for items whose keys were seeded by other means"*; add `string? oracleId = null` as the last `From` parameter.
  - `AssessmentDbContext`: `item.Property(i => i.OracleId).HasMaxLength(128);`
  - Migration: `dotnet ef migrations add ItemOracleId --project src/Modules/Praxis.Modules.Assessment --startup-project src/Praxis.Api` (match the exact invocation used by existing migrations if different — check the repo's previous migration commits).
  - `SubmitAnswer.cs:107`: `ItemId: item.OracleId ?? item.Id.ToString(),`
  - Contract records as above; `AiPlaneClient.GenerateItemAsync` passes the new field through (it serializes the request record — confirm the JSON naming policy matches what FastAPI expects, same as `AlreadySeen` → `already_seen` today).
  - Update the four fakes to the new record shapes (add `TargetsMisconception: null` etc.).
  - Update `AiPlaneContractTests` **deliberately** — pin the two new fields.
- [ ] **Step 4: Run tests** — `dotnet test Praxis.slnx --nologo -v quiet` → green, including `SchemaTests` (the new column is `oracle_id` — an identifier, not an answer; if the paranoid sweep objects to the word, do NOT weaken the sweep: rename the column to `marker_item_id` instead and re-run).
- [ ] **Step 5: Full gate** — `.\scripts\verify.ps1`.
- [ ] **Step 6: Tick + commit** — `git commit -m "M14.3 - the wire carries declared wrong beliefs out and a hunted key back; a banked item remembers its oracle name"`

---

### Task 4 (M14.4): The concept-intel seam — Assessment asks, the Graph answers, neither knows the other

**Files:**
- Create: `src/Modules/Praxis.Modules.Assessment/IConceptIntel.cs`
- Create: `src/Praxis.Api/Composition/GraphConceptIntel.cs`
- Modify: `src/Praxis.Api/Program.cs` (register, next to `GraphMisconceptionLibrary` — grep for it)
- Test: `tests/Praxis.Api.Tests/` (an integration test against the real graph, mirroring how `GraphMisconceptionLibrary` was proven — grep `GraphMisconceptionLibrary` in `tests/` and follow that test's seeding pattern)

**Interfaces:**
- Consumes: `GraphDbContext.Concepts` (`Summary`, `DifficultyPrior` — logits, roughly −3..+3) and `GraphDbContext.Misconceptions` (`ConceptId`, `Key`, `Statement`).
- Produces:

```csharp
namespace Praxis.Modules.Assessment;

/// What the item generator needs to know about a concept, from whoever holds it.
/// A port, like the tutor's IMisconceptionLibrary (M13.2b-ii): Assessment names what it needs;
/// the composition root decides where it comes from; no module reads another module's tables.
public sealed record ConceptIntel(
    string Summary,
    /// The generator's difficulty scale is 0..1; the pack's prior is an IRT logit. Mapped here,
    /// once: (prior + 3) / 6, clamped.
    double DifficultyTarget,
    IReadOnlyDictionary<string, string> DeclaredMisconceptions);

public interface IConceptIntel
{
    /// Null when the Graph has never heard of the concept — the caller refuses, it does not invent.
    Task<ConceptIntel?> ForAsync(Guid conceptId, CancellationToken ct = default);
}
```

- [ ] **Step 1: Write the failing test** — integration, real Postgres + the shipped pack (copy the arrangement of the existing `GraphMisconceptionLibrary` test): `bm25`'s intel has its summary, a `DifficultyTarget` within (0,1), and `bm25-is-obsolete` in `DeclaredMisconceptions`; an unknown Guid returns null.
- [ ] **Step 2: Run to verify failure** → FAIL: `IConceptIntel` does not exist.
- [ ] **Step 3: Implement** `GraphConceptIntel`:

```csharp
public sealed class GraphConceptIntel : IConceptIntel
{
    private readonly GraphDbContext _db;

    public GraphConceptIntel(GraphDbContext db) => _db = db;

    public async Task<ConceptIntel?> ForAsync(Guid conceptId, CancellationToken ct = default)
    {
        var concept = await _db.Concepts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conceptId, ct);

        if (concept is null)
        {
            return null;
        }

        var declared = await _db.Misconceptions.AsNoTracking()
            .Where(m => m.ConceptId == conceptId)
            .OrderBy(m => m.Key)
            .ToDictionaryAsync(m => m.Key, m => m.Statement, ct);

        return new ConceptIntel(
            concept.Summary,
            Math.Clamp((concept.DifficultyPrior + 3.0) / 6.0, 0.0, 1.0),
            declared);
    }
}
```

Register: `services.AddScoped<Praxis.Modules.Assessment.IConceptIntel, GraphConceptIntel>();` beside the `IMisconceptionLibrary` registration. (Check the `Concept`/`ConceptMisconception` property names against the entities before assuming — `Statement` in particular.)
- [ ] **Step 4: Run tests** → PASS. **Step 5:** `.\scripts\verify.ps1`. **Step 6:** `git commit -m "M14.4 - the concept-intel seam: assessment asks for summary, difficulty and declared wrong beliefs without knowing the Graph exists"`

---

### Task 5 (M14.5): `EnsureConceptPool` — the banking slice

**Files:**
- Create: `src/Modules/Praxis.Modules.Assessment/Features/Diagnostics/EnsureConceptPool.cs`
- Modify: `src/Modules/Praxis.Modules.Assessment/Features/Diagnostics/DiagnosticsController.cs` (new route + request record + validator)
- Test: `tests/Praxis.Modules.Assessment.Tests/EnsureConceptPoolTests.cs`

**Interfaces:**
- Consumes: `IAiPlane.GenerateItemAsync` (Task 3's shape), `IConceptIntel` (Task 4), `AssessmentDbContext.Items/Responses`, `BankedItem.From(..., oracleId)`.
- Produces: `POST api/assessment/diagnostics/pool` body `{ conceptId }` → `PoolResponse(int Generated, int PoolSize)`. Constant `EnsureConceptPool.PoolFloor = 5`. Task 6 calls the route; Task 7 proves it end to end.

- [ ] **Step 1: Write the failing tests** (`EnsureConceptPoolTests.cs`, against a real `AssessmentDbContext` the way `RecordTeachBackTests` does — read that file first and reuse its harness):

```csharp
// 1. A thin pool is topped up to the floor: 1 unseen item banked, floor 5 → exactly 4 generate
//    calls; 4 new rows; each row's OracleId = the response Id, TargetsMisconception carried,
//    Difficulty mapped back to logits (response 0.5 → 0.0 stored).
// 2. A full pool generates nothing: 5 unseen banked → zero calls to the AI plane, Generated = 0.
// 3. What the learner has answered is not "unseen": 5 banked but 3 answered by this learner →
//    2 unseen → 3 generate calls.
// 4. The generator is handed the bank's fingerprints: every banked fingerprint for the concept
//    appears in the captured GenerateItemRequest.AlreadySeen (banking a duplicate stem is refused
//    upstream by the unique index; passing the fingerprints means it never gets that far).
// 5. The declared wrong beliefs travel: captured request.KnownMisconceptions == the intel's map.
// 6. A generation failure banks nothing and carries the failure: fake IAiPlane fails on call 1 →
//    zero rows, result is the carried failure (never a success wearing an empty pool).
// 7. A partial failure keeps the real items: calls 1-2 succeed, call 3 fails → 2 rows banked,
//    response Generated = 2 (real items are not thrown away because a later one failed).
// 8. An unknown concept is NotFound: IConceptIntel returns null → NotFound, no generate calls.
```

Write all eight as real tests with a capturing fake `IAiPlane` (model it on `FakeOracle` in the same test project).

- [ ] **Step 2: Run to verify failure** → FAIL: `EnsureConceptPool` does not exist.
- [ ] **Step 3: Implement the slice** (shape it exactly like `SubmitAnswer`):

```csharp
public static class EnsureConceptPool
{
    public sealed record Command(Guid ConceptId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator() => RuleFor(c => c.ConceptId).NotEmpty();
    }

    public sealed record PoolResponse(int Generated, int PoolSize);

    /// How many unseen items a learner's diagnostic should have waiting. Below this, PRAXIS
    /// writes more; at it, calling this endpoint is free and does nothing.
    public const int PoolFloor = 5;

    public sealed class Handler
    {
        private readonly AssessmentDbContext _db;
        private readonly IAiPlane _ai;
        private readonly IConceptIntel _intel;
        private readonly IUserContext _user;

        public Handler(AssessmentDbContext db, IAiPlane ai, IConceptIntel intel, IUserContext user)
            => (_db, _ai, _intel, _user) = (db, ai, intel, user);

        public async Task<Result<PoolResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var intel = await _intel.ForAsync(command.ConceptId, ct);
            if (intel is null)
            {
                return Result<PoolResponse>.NotFound(
                    "No such concept. PRAXIS does not write items about things the Graph has "
                    + "never heard of.");
            }

            var seenByLearner = await _db.Responses.AsNoTracking()
                .Where(r => r.LearnerId == learner && r.ConceptId == command.ConceptId)
                .Select(r => r.ItemId).Distinct().ToListAsync(ct);

            var banked = await _db.Items.AsNoTracking()
                .Where(i => i.ConceptId == command.ConceptId)
                .Select(i => new { i.Id, i.Fingerprint }).ToListAsync(ct);

            var unseen = banked.Count(i => !seenByLearner.Contains(i.Id));
            if (unseen >= PoolFloor)
            {
                return Result<PoolResponse>.Success(new PoolResponse(0, unseen));
            }

            // Every fingerprint already in the bank: novelty against the whole bank, not just this
            // learner — a duplicate stem would be the same item wearing a new Guid.
            var fingerprints = banked.Select(i => i.Fingerprint).ToList();

            var generated = new List<BankedItem>();
            Result<PoolResponse>? failed = null;

            for (var needed = PoolFloor - unseen; generated.Count < needed;)
            {
                var response = await _ai.GenerateItemAsync(
                    new GenerateItemRequest(
                        ConceptId: command.ConceptId.ToString(),
                        ConceptSummary: intel.Summary,
                        EvidenceType: EvidenceType.Apply,
                        Difficulty: intel.DifficultyTarget,
                        LearnerId: learner.ToString(),
                        AlreadySeen: fingerprints,
                        KnownMisconceptions: intel.DeclaredMisconceptions),
                    ct);

                if (!response.IsSuccess)
                {
                    // Real items already made are kept; the failure is only fatal when it left
                    // nothing behind. A learner is never refused because the *sixth* item failed.
                    failed = response.CarryFailure<PoolResponse>();
                    break;
                }

                var item = response.Value;
                fingerprints.Add(item.Fingerprint);

                generated.Add(BankedItem.From(
                    Guid.CreateVersion7(),
                    command.ConceptId,
                    item.EvidenceType,
                    item.Stem,
                    item.Fingerprint,
                    difficulty: (item.Difficulty * 6.0) - 3.0,   // generator 0..1 → IRT logits
                    targetsMisconception: item.TargetsMisconception,
                    oracleId: item.Id));
            }

            if (generated.Count == 0 && failed is not null)
            {
                return failed;
            }

            _db.Items.AddRange(generated);
            await _db.SaveChangesAsync(ct);   // the one save

            return Result<PoolResponse>.Success(
                new PoolResponse(generated.Count, unseen + generated.Count));
        }
    }
}
```

Controller (in `DiagnosticsController`, following the M6.5b rule — validator on the bound record):

```csharp
    /// **Make sure there is something honest to ask.** Below the floor, PRAXIS writes new items
    /// via the AI plane — the answer goes to the oracle's store on the far side and never comes
    /// back here. Skipping this call breaks nothing: `next-item` keeps its refusal.
    [HttpPost("pool")]
    public async Task<IActionResult> Pool(
        [FromBody] PoolRequest request,
        [FromServices] EnsureConceptPool.Handler handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await handler.HandleAsync(new EnsureConceptPool.Command(request.ConceptId), ct))
            .ToApiResponse();
    }

public sealed record PoolRequest(Guid ConceptId);

public sealed class PoolRequestValidator : AbstractValidator<PoolRequest>
{
    public PoolRequestValidator() => RuleFor(r => r.ConceptId).NotEmpty();
}
```

(Note: `BankedItem.From`'s existing signature has `discrimination`, `guessing`, `familyId` before `targetsMisconception` — use named arguments as shown so the defaults hold.)
- [ ] **Step 4: Run tests** → all 8 pass. **Step 5:** `.\scripts\verify.ps1`. **Step 6:** `git commit -m "M14.5 - EnsureConceptPool: a thin pool is topped up with fresh items whose answers this process never saw"`

---

### Task 6 (M14.6): The web diagnostic keeps its pool filled

**Files:**
- Modify: `web/lib/api.ts:173-185` (the `diagnostics` client)
- Modify: `web/lib/diagnostic.ts` + the `/diagnostic` page component (read both first; the flow is `nextItem → answer → nextItem`)
- Test: the existing diagnostic test files under `web/` (extend, following their mock patterns)

**Interfaces:**
- Consumes: Task 5's `POST /api/assessment/diagnostics/pool` → `{ generated, poolSize }` inside the standard `ApiResponse` envelope.
- Produces: `diagnostics.ensurePool(conceptId, token?, signal?)` in `api.ts`; the page calls it before the first `nextItem` and after each submitted answer (before the next `nextItem`).

- [ ] **Step 1: Write the failing tests** — extend the diagnostic page/engine tests: (a) starting a diagnostic calls `ensurePool` before the first `nextItem` (mock call order); (b) an `ensurePool` failure still proceeds to `nextItem` — the pool call is best-effort, and `nextItem`'s own refusal stays the honest gate (assert no error screen from the pool call alone); (c) after an answer, `ensurePool` fires again before the next item.
- [ ] **Step 2: Run to verify failure** — `cd web; npm run test` → FAIL (`ensurePool` is not a function).
- [ ] **Step 3: Implement** — in `api.ts`:

```typescript
export interface PoolResponse {
  generated: number;
  poolSize: number;
}

// ...inside `diagnostics`:
  ensurePool: (conceptId: string, token?: string, signal?: AbortSignal) =>
    api.post<PoolResponse>(
      `/api/assessment/diagnostics/pool`,
      { conceptId },
      token,
      signal,
    ),
```

(Match the real `api.post` signature — read it first.) In the page/engine: `await diagnostics.ensurePool(...).catch(() => undefined)` before each `nextItem` — a failed top-up is not an error the learner sees; an empty pool after it is `nextItem`'s refusal, which the page already renders honestly.
- [ ] **Step 4: Run tests** — `npm run test` → green. **Step 5:** `.\scripts\verify.ps1`. **Step 6:** `git commit -m "M14.6 - the diagnostic keeps its pool filled, and a failed top-up is never an error screen"`

---

### Task 7 (M14.7): The end-to-end proof from an empty bank

**Files:**
- Create: `tests/Praxis.Api.Tests/ItemBankE2ETests.cs`
- Modify: `PLAN.md` (tick M14.7 and close the M14 section with the summary note)

**Interfaces:**
- Consumes: everything above, through the real host (`WebApplicationFactory` — copy the arrangement from `EndToEndJourneyTests`).

- [ ] **Step 1: Write the test** — a scripted, capturing fake `IAiPlane` (like `LeakingAiPlane`): `GenerateItemAsync` returns deterministic items (`item-e2e-1`…, unique stems/fingerprints, `TargetsMisconception: null`); `CheckClaimAsync` returns `Correct` **and captures the request**. Then, as one journey:

```csharp
// 1. Register a learner; confirm the bank is empty for the concept (no seeding).
// 2. POST /api/assessment/diagnostics/pool { conceptId } → 200, generated == 5.
// 3. GET  /api/assessment/diagnostics/{session}/next-item?conceptId= → 200 with one of the
//    generated stems (not Stopped, not a refusal).
// 4. POST /api/assessment/diagnostics/{session}/answers → 200, outcome "Correct".
// 5. The oracle was asked under the generated item's oracle id ("item-e2e-1"), not our Guid.
// 6. The immutable log now holds an AnswerGiven for this learner and concept.
// 7. The adversarial sweep: serialize every captured request and every response body seen in this
//    test; assert none contains the strings "answer\":" / "solution" / "expected" (the generate
//    path carries wrong-belief keys and statements — beliefs are not answers — so sweep the
//    JSON property names, mirroring how the existing answer-sweep tests do it).
```

Also a second test: `Pool_generation_failure_banks_nothing_and_the_diagnostic_refuses_honestly` — fake fails all generation; POST pool carries the failure; next-item still refuses (no invented item).
- [ ] **Step 2: Run to verify it fails for the right reason first** — comment nothing out; if all prior tasks landed, it should pass immediately. If it fails, the failure is a real wiring bug: fix forward, never weaken the assertion.
- [ ] **Step 3: Full gate** — `.\scripts\verify.ps1` → every stage.
- [ ] **Step 4: Close the milestone** — tick M14.7 in PLAN.md and update the `## Status` block: the diagnostic now runs end to end on generated, banked, durable-keyed items.
- [ ] **Step 5: Commit** — `git commit -m "M14.7 - the end-to-end proof: an empty bank fills itself, the diagnostic runs, and the answer never crossed the wire"`
