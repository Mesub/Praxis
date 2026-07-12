# PRAXIS

**The Mastery Operating System.**

> Learning is a build system. The goal is the build target. The knowledge graph is the dependency
> DAG. What you already know is the cache. **Forgetting is cache invalidation.**
>
> PRAXIS is the compiler, the scheduler, and the test suite — and it never lets you ship a build
> that didn't pass its tests.

**There is no "mark as complete" button in PRAXIS.** Anywhere. It does not exist. Nothing is
recorded as learned unless the learner *produced evidence* — solved a never-seen problem, explained
it aloud, shipped something that runs, found the flaw, or predicted the real world and reality
agreed.

Everything else follows from that one refusal. The full argument is in [`docs/`](docs/); the
non-negotiables are in [`docs/CANON.md`](docs/CANON.md).

---

## Run it

You need **.NET 10**, **Docker Desktop**, and — later, once the AI plane and web console land —
**Python 3.12+** and **Node 20+**.

```powershell
git clone <this repo> && cd Praxis

# Postgres + the API, with a health check that waits for the database to be genuinely ready.
.\scripts\dev.ps1
```

That brings up PostgreSQL 17 (with `pgvector`) and runs the API. To bring up only the database:

```powershell
.\scripts\dev.ps1 -InfraOnly
```

To stop, and optionally destroy the data:

```powershell
.\scripts\dev.ps1 -Down            # stop the containers
.\scripts\dev.ps1 -Down -Volumes   # ...and delete the event log. There is no undo.
```

**Postgres listens on `localhost:5433`**, not the default 5432 — other projects on this machine
already own that port.

## Verify it

```powershell
.\scripts\verify.ps1          # everything: .NET, the Python AI plane, the web console
.\scripts\verify.ps1 -Quick   # .NET only, for a fast inner loop
```

Nothing is "done" until this passes. Stages that don't exist yet report **SKIP**, by name, and the
verdict says so — *a skip is not a pass.*

## Where things are

| Path | What lives there |
|---|---|
| `docs/` | The blueprint. `CANON.md` wins every argument. |
| `PLAN.md` | The Phase 0 build plan, and what has actually landed. |
| `CLAUDE.md` | Build conventions. Read before changing code. |
| `src/Praxis.Api/` | The host. Thin controllers; no logic. |
| `src/Praxis.Shared/` | `Result<T>`, the HTTP envelope, `BaseEntity`, `IClock`. |
| `src/Praxis.Infrastructure/` | DI, validation, the global exception handler. |
| `src/Modules/` | One project per engine. Each owns its schema. Modules never touch each other's tables. |
| `ai/` | The Python AI plane (arrives at M5). |
| `web/` | The Next.js console (arrives at M11). |
| `packs/` | Domain packs — the concept graph, PoM contracts, item banks. |
| `prototype/` | The clickable mock of the product. Open it in a browser. |

## The architecture, in one breath

Every learner interaction is an **immutable event** appended to one log. **The Cognitive Twin — what
we believe the learner knows — is a projection of that log, rebuildable from zero.** Mastery is not
a column someone can set; it is *derived*, only ever from evidence, and it *decays*.

That is why the refusals are structural rather than aspirational:

- The tutor **cannot** hand over the answer, because the answer is never placed in its context. It
  calls an oracle. A jailbreak retrieves nothing, because there is nothing there to retrieve.
- An AI grader **cannot** manufacture mastery, because LLM-graded evidence is mathematically capped
  below the threshold that counts as *held*. Only a never-seen problem solved, code that actually
  ran, or a prediction reality resolved can cross it.
- We **cannot** sell a plan we don't believe in, because the compiler forward-simulates it, and says
  so out loud when your available minutes make the goal unreachable.

## Status

Phase 0 — see [`PLAN.md`](PLAN.md) for what has landed and what is next.

---

*No catalog. No completion button. Evidence only.*
