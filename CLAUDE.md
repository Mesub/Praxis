# PRAXIS — build conventions

Read this before every work session. `docs/CANON.md` is the product source of truth;
`D:\claude\ARCHITECTURE_STANDARD.md` is the engineering source of truth. This file is the
short version plus the decisions we made for the Phase 0 build.

## What we are building

The **Phase 0 slice** from `docs/25-roadmap.md §1.2`: the smallest thing that is genuinely PRAXIS.

Ships: **E1 Twin · E2 Graph · E3 Compiler · E4 Scheduler · E5 Tutor (Answer Budget) ·
E6 Assessment · E7 Memory · E8 Reality Loop (code only) · E16 Ledger**, on the web (Next.js).

Does **not** ship: mobile, offline, i18n, payments, institutions, news, projects, career,
markets/medicine/law Loops, counterfactual Twin queries, self-healing Graph.

## The refusals — enforce these in code, not in prompts

| Refusal | How it is enforced |
|---|---|
| No "mark as complete" | No such endpoint, no such column, no such event type. Mastery only moves via `Evidence`. |
| The tutor cannot give the answer | The answer is **never placed in the tutor's LLM context** (C-5). The tutor calls a `check_claim` oracle. A jailbreak retrieves nothing because nothing is there. |
| LLM grading cannot manufacture mastery | The **Evidence Ceiling** (C-4): `EXPLAIN`-type evidence can raise belief but is mathematically incapable of crossing the HOLD threshold alone. Only `APPLY` / `BUILD` / `PREDICT` can. |
| We do not sell a plan we don't believe in | The **Decay Wall** (C-1): if the compiler's forward simulation diverges at the learner's stated minutes/day, we say so and narrow the Objective. |

If a change would make any of these easier to violate, it is wrong, regardless of how well it works.

## Stack (decided)

| Layer | Choice |
|---|---|
| Core | .NET 10, modular monolith + vertical slice, one project per Engine |
| AI plane | Python 3.14 + FastAPI, stateless, talks to the Claude API |
| Web | Next.js (App Router) + TypeScript + Tailwind |
| Storage | **PostgreSQL only** for now — system of record, event store, `pgvector` for embeddings, recursive CTEs for graph traversal |
| Later (only when it hurts) | Redis, Redpanda, ClickHouse, Apache AGE |
| Models | Claude via the API. Opus-class for compilation/grading/synthesis; Sonnet/Haiku-class for tutor turns and item generation. |

Secrets live in `ai/.env` and `src/Praxis.Api/appsettings.Development.json` — both gitignored.
Never commit a key. Never print a key.

**Local Postgres runs on port 5455.** Not 5432 (another project's container owns it) and not 5433
(a native Windows PostgreSQL service is already listening there — Docker will bind alongside it
and then lose the race for incoming connections, which surfaces as a *password* failure and sends
you hunting for a credential bug that does not exist). Check a port is genuinely free before
claiming it: `netstat -ano | Select-String ":<port>\s.*LISTENING"`.

Connection string: `Host=localhost;Port=5455;Database=praxis;Username=praxis;Password=praxis_dev_only`

Each engine owns a **schema** (`kernel`, `graph`, `twin`, `compiler`, `tutor`, `assessment`,
`memory`, `reality`, `ledger`, `identity`), created in `scripts/db/init/01-extensions.sql`.
No module reads another module's tables.

## Architecture rules (from ARCHITECTURE_STANDARD.md — non-negotiable)

- **Vertical slice.** One use case = one file = `Command`/`Query` + `Validator` + `Handler` + `Response`.
- **No mediator.** Handlers are injected into thin controllers with `[FromServices]`.
- **No AutoMapper.** Manual extension-method mapping, or Mapperly.
- `Result<T>` for expected failures; exceptions only for bugs and infrastructure faults.
- Controllers never build `ApiResponse` by hand — they call `.ToApiResponse()`.
- Audit fields, tenant stamping and soft delete are set by the **interceptor**, never by hand.
- One handler = one use case = one `SaveChangesAsync`.
- Permissions: `{module}.{resource}.{action}`. Routes: `api/{module}/{kebab-plural}`.

## The event kernel

Every learner interaction is an **immutable Learning Event** appended to one table. **The Twin is a
projection of the event log and must be rebuildable from zero.** If you add state to the Twin that
cannot be recomputed by replaying events, you have broken the kernel.

## Definition of done for any task

1. Code follows the slice pattern above.
2. **Tests exist and pass** — handler tests at minimum; integration tests for anything crossing a boundary.
3. `scripts/verify.ps1` passes (build + test + lint, all three stacks).
4. The task is ticked in `PLAN.md` with a one-line note on what actually landed.
5. Committed, with the milestone id in the message.

Never claim something works without having run it. Evidence before assertions — the same standard
we hold learners to.
