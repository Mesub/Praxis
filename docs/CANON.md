# PRAXIS — CANON

> The single source of truth. Every other document must obey this file.
> If a doc contradicts CANON, CANON wins.

---

## 0. Name & Category

**Product:** PRAXIS
**Tagline:** *The Mastery Operating System.*
**Category (new, invented):** **Mastery OS** — not an LMS, not a course marketplace, not a
chatbot. An operating system whose *processes* are learners' minds, whose *filesystem* is a
knowledge graph, whose *scheduler* is a forgetting-aware planner, and whose *unit of truth* is
**verified evidence of understanding**.

**The one-sentence thesis:**
> Learning is a build system. The goal is the build target. The knowledge graph is the
> dependency DAG. What you already know is the cache. **Forgetting is cache invalidation.**
> PRAXIS is the compiler, the scheduler, and the test suite — and it never lets you ship
> a build that didn't pass its tests.

**The category-defining refusal:**
> Every other product optimizes for *content consumed*. PRAXIS refuses to record anything as
> learned unless the learner *produced evidence*. There is no "mark as complete" button in the
> entire product. It does not exist. This is the product.

---

## 1. The Ten Inventions (the differentiation — non-negotiable)

Every feature in every document must trace back to one of these. If it doesn't, cut it.

### I-1 — The Cognitive Twin
Not a "user profile." A **live probabilistic simulator of the learner's mind**, at concept
granularity. For every concept `c` the Twin holds:
`P(knows(c))`, a **decay curve** (when that probability falls below threshold), a **confidence
vs. competence gap** (Dunning–Kruger delta), an **evidence ledger** (what proved it), and a
**misconception set** (the specific wrong models this learner holds).
The Twin is **queryable and counterfactual**: *"If she studies 30 min/day, when is she
job-ready as an AI engineer? What breaks if she skips linear algebra?"* Every engine in PRAXIS
is a **policy acting on the Twin**. The Twin is the kernel's process table.

### I-2 — Proof-of-Mastery (PoM) contracts
Every concept node ships with a **contract**: the *specific kind of evidence* that will be
accepted as proof of understanding it. Six evidence types:
`RECALL` · `APPLY` (novel item, never-seen) · `EXPLAIN` (Feynman teach-back, rubric-graded) ·
`BUILD` (artifact that runs / passes) · `CRITIQUE` (find the flaw in a wrong worked example) ·
`PREDICT` (forecast reality; graded when reality resolves).
Mastery is **never binary and never permanent**. It is a **decaying credit** with a confidence
level. You do not "complete" a concept — you *hold* it, and it *bleeds* unless refreshed.

### I-3 — The Reality Loop (the crown jewel)
The learner is required to make **falsifiable predictions about the real world**, which are
later scored **against ground truth when reality resolves**.
- Trading: mark the chart *today*; tomorrow the market grades you.
- Programming: your code must actually run against hidden tests.
- Medicine: diagnose a real (de-identified) case; the actual outcome grades you.
- Law: predict the ruling; the real judgment grades you.
- Business: forecast the quarter; the filing grades you.
Scored with **Brier score → Calibration**, a first-class metric next to Mastery.
*No learning platform on earth lets reality be the examiner. This one does.*

### I-4 — The Curriculum Compiler
A roadmap is not authored — it is **compiled**. Target (`Become X`) + dependency DAG + Twin
(cache) + constraints (minutes/day, deadline, learning velocity, decay pressure) → an optimal
**shortest-path-to-competence** plan. It is **recompiled nightly** (and on every significant
event). Skipping a lesson doesn't break a rigid path; it **changes the compile**.
`make` for the mind, with cache invalidation driven by the forgetting curve.

### I-5 — The Answer Budget (anti-ChatGPT, enforced in architecture)
The tutor is **structurally forbidden** from giving the answer. Not by a prompt — by a **policy
layer** the model cannot bypass: a **hint ladder** (`orient → probe → analogy → partial → full`)
where each rung costs the learner from a per-concept **answer budget**, and the tutor's job is
to make the *learner* produce the answer. Every session ends with the learner having *said* or
*built* something, not having *read* something.
> The AI's job is not to produce. It is to make the human produce.

### I-6 — Failure-Seeking Assessment
The assessment engine is adversarial by design: it **hunts the boundary of your knowledge**
(adaptive/IRT), then **mines your wrong answers into a personal Misconception Library**, then
**generates new items specifically targeting your misconceptions**. It is not trying to give
you a good score. It is trying to find where you break.

### I-7 — News-as-Curriculum
The feed is not a feed. Each real-world event is decomposed into concept nodes, diffed against
the Twin, and either (a) **taught**, if you have the prerequisites, (b) **deferred with a
prerequisite ladder**, if you don't, or (c) **used as a spaced-repetition rep** for concepts
you already hold. Every article ends in a question or a prediction.
> Your feed is a revision schedule wearing a news app's clothes.

### I-8 — The Evidence Ledger (the moat & the money)
Append-only, tamper-evident, **cryptographically signed** record of every piece of evidence the
learner ever produced. Exportable as a **Verified Competency Record** — a résumé that a third
party can *verify*, item by item ("this human demonstrably diagnosed 41 novel cases at 0.79
calibration"). Universities, employers, and regulators buy this. Learners own it.

### I-9 — Commitment Contracts (accountability with teeth)
The learner signs a contract ("45 min/day, 6 days/week, AI Engineer by March"). PRAXIS holds
them to it: escalating nudges, streak & debt mechanics, optional **stakes** (money/social),
optional **witness** (parent / manager / mentor gets the dashboard). A strict teacher, not a
notification.

### I-10 — Mastery-per-Minute
The single north-star metric of the whole OS. Time is the learner's scarcest resource; the
scheduler optimizes **verified mastery gained per minute invested**, and the product reports it
honestly — including when it is *low*, and why. Engagement is not a metric. Time-on-app is not
a metric. **We optimize for the learner leaving.**

---

### I-11 — The Resident Agent (PRAXIS is an agent that has an app, not an app that has an agent)

**An app waits to be opened. A teacher does not.**

The single hardest moment in learning is *starting*, and an app hands that exact moment back to
the person who is demonstrably bad at it. So PRAXIS's primary form is **not a screen** — it is a
**persistent, autonomous agent** that runs continuously on the learner's behalf, whether or not
they ever open anything.

It has three modes of existence:

| Mode | What it means | Example |
|---|---|---|
| **Resident** | Always running. Watches, models, plans, prepares. | Overnight: recompiles the Plan, generates tomorrow's never-seen items, pre-fetches sources, resolves yesterday's Reality Loops, checks decay. |
| **Reaching** | It initiates. It comes to *you*, on the surface you already live on. | *WhatsApp, 7:12am:* "Market opens in 18 minutes. You have an open prediction on RELIANCE that resolves today. 60 seconds — want the setup?" |
| **Acting** | It does work in the world, with permission, and reports back. | Opens a PR review on your repo. Books the 25-minute slot it found in your calendar. Watches for the paper you needed. Emails your mentor the weekly evidence report you agreed to. |

**The Agent Loop** (runs continuously, not on app-open):

```
observe   → events, calendar, market open, your repo, the news, your silence
model     → update the Twin; decay is happening right now, whether you log in or not
decide    → is there something worth interrupting a human for? (the bar is HIGH)
act       → prepare · reach out · do work · resolve a loop · escalate to the Witness
learn     → did that intervention actually produce mastery? if not, never do it again
```

**The Autonomy Ladder** — the learner grants autonomy in rungs, and the agent *earns* the next
rung by being right:

| Rung | The agent may… | Default |
|---|---|---|
| **A0 — Observe** | model you, plan, prepare. Never speak. | on |
| **A1 — Suggest** | initiate contact. Nudge, brief, ask. | on |
| **A2 — Prepare** | do prep work unasked (generate items, draft a project brief, pre-read a paper, open a Loop). | on |
| **A3 — Act (reversible)** | book a calendar slot, open a draft PR, file the rep queue, message your Witness. | opt-in |
| **A4 — Act (consequential)** | submit a real code review, register you for an exam, place a simulated trade, spend money against a stake. | explicit, per-action, revocable |

**Where it lives.** Not just an app: **WhatsApp / SMS / voice call · email · calendar ·
your IDE (it reviews your real commits) · your browser · a watch · a phone call you can answer
while walking.** The app is the *console*. The agent is the product.

**The interruption bar.** An agent that can reach you is an agent that can *ruin* you. Every
proactive contact must pay for itself in **mastery**, not opens. Each nudge type is A/B'd on
delayed-retention outcome; **the highest-open-rate nudge we ever shipped was killed for producing
negative mastery.** The agent's right to speak is a budget it can spend down to zero.

**Why nobody has done it.** Because a proactive agent is terrifying to a company measured on
engagement — it *reduces* app opens by design (it does the work elsewhere). And because an agent
that acts requires you to be *right*; a passive app can be wrong for years and nobody notices.

**What it kills.** The abandoned subscription. "I forgot I had it." The 4% of learners who ever
open the app twice.
→ `26-the-agent.md`

---

## 2. What PRAXIS explicitly is NOT

| Not this | Because |
|---|---|
| A video course catalog | Content is a *means*, sourced/generated on demand. There is no catalog. |
| A chatbot | The tutor is under policy control; it cannot just answer. |
| A quiz app | Quizzes are one of six evidence types, and the weakest one. |
| An engagement machine | Streaks serve consistency, never retention-of-user metrics. |
| A "complete the course" tracker | There is no completion. Only decaying, evidenced mastery. |

---

## 3. Core Domain Language (use these exact terms everywhere)

| Term | Meaning |
|---|---|
| **Objective** | What the learner wants to *become* ("AI Engineer"). The build target. |
| **Concept** | An atomic node of knowledge. The unit of the graph and of mastery. |
| **Skill** | A composite capability made of concepts + demonstrated application. |
| **Graph** | The global, versioned dependency DAG of concepts (`REQUIRES`, `RELATES`, `CONTRASTS`, `APPLIES_TO`). |
| **Twin** | The learner's cognitive digital twin (see I-1). |
| **Mastery Credit (MC)** | Decaying, evidence-backed confidence in one concept. `0.0–1.0`. |
| **Evidence** | A signed, immutable record of something the learner *produced* (see I-2). |
| **PoM Contract** | The evidence types & thresholds required to hold a concept. |
| **Compile** | Producing/refreshing the plan from Objective + Graph + Twin + Constraints. |
| **Plan** | The compiled forward schedule (today → deadline). Ephemeral, always regenerable. |
| **Session** | One bounded learning block (5/10/15/25 min). The scheduling atom. |
| **Rep** | One spaced-repetition retrieval event on a concept. |
| **Loop** | A Reality Loop prediction, open until reality resolves it. |
| **Calibration** | How well the learner's confidence matches their correctness (Brier). |
| **Misconception** | A named, specific wrong mental model held by this learner. |
| **Contract** | The learner's commitment (time, cadence, deadline, stake, witness). |
| **Ledger** | The append-only evidence chain. |

---

## 4. The Engines (the OS "kernel modules")

Each is an independent module with its own schema, subscribing to and emitting events.

| # | Engine | One-line responsibility |
|---|---|---|
| E1 | **Twin Engine** | Maintains `P(knows)`, decay, calibration, misconceptions. The projection of all events. |
| E2 | **Graph Engine** | Owns the concept DAG, prerequisites, versioning, domain packs. |
| E3 | **Compiler Engine** | Compiles Objective+Twin+Constraints → Plan. Recompiles nightly/on-event. |
| E4 | **Scheduler Engine** | Chooses *what to do in the next 15 minutes*. Merges new-learning vs. reps vs. loops vs. projects. |
| E5 | **Tutor Engine** | Socratic dialogue under the Answer Budget policy. Multi-modal. Personality-adaptive. |
| E6 | **Assessment Engine** | Item generation (IRT-calibrated), adaptive testing, rubric grading, misconception mining. |
| E7 | **Memory Engine** | Forgetting-curve modeling (FSRS-class + Twin-informed), rep scheduling. |
| E8 | **Reality Engine** | Live-world tasks, prediction capture, resolution against ground truth, Brier scoring. |
| E9 | **News Engine** | Ingest → concept extraction → Twin diff → teach / defer / rep. |
| E10 | **Research Engine** | Multi-source retrieval, source-trust scoring, disagreement surfacing, fact vs. opinion. |
| E11 | **Project Engine** | Briefs, artifact intake, automated + AI review, portfolio, peer review. |
| E12 | **Career Engine** | Role ontology → required skills → gap analysis → interview simulation → market signal. |
| E13 | **Motivation Engine** | Burnout detection, load adjustment, streaks/debt, celebration, stakes. |
| E14 | **Accountability Engine** | Contracts, escalation ladder, witness dashboards. |
| E15 | **Analytics Engine** | Mastery-per-minute, growth, calibration, consistency, ROI. Learner + org dashboards. |
| E16 | **Ledger Engine** | Signed, append-only evidence; verifiable competency export. |
| E17 | **Content Engine** | On-demand lesson synthesis (never a static catalog), grounded in Research + Graph. |
| E18 | **Notification Engine** | The right nudge, at the right minute, on the right device. |

---

## 5. Architecture decisions (locked)

1. **Event-sourced kernel.** Every learner interaction is an immutable **Learning Event**
   (`ConceptTaught`, `AnswerGiven`, `ExplanationRecorded`, `RepDue`, `PredictionMade`,
   `PredictionResolved`, `ArtifactSubmitted`, `SessionAbandoned`, …). **The Twin is a
   projection of the event log.** Rebuildable from zero. This is also how the Ledger is
   trustworthy and how research/analytics stay honest.
2. **Modular monolith + vertical slice** for the transactional core (`PRAXIS.Core`, .NET 10,
   one project per Engine, one `DbContext`/schema each, slices as use cases) —
   consistent with `D:\claude\ARCHITECTURE_STANDARD.md`.
3. **Python AI plane** (FastAPI) for the model-heavy work: item generation, rubric grading,
   concept extraction, embedding, retrieval, tutor orchestration, psychometrics (IRT/BKT/FSRS).
   Core talks to it over a stable contract; it is stateless and horizontally scalable.
4. **Storage:** PostgreSQL (system of record + event store) · **pgvector** (embeddings) ·
   **Apache AGE / Neo4j** (concept graph traversal) · Redis (hot Twin cache, session state) ·
   S3-compatible object store (artifacts, audio, drawings) · ClickHouse (analytics).
5. **Bus:** Kafka/Redpanda topics per event family. Engines are consumers. No engine calls
   another engine's internals — ever.
6. **AI models:** router across tiers — a large reasoning model (Claude Opus-class) for
   compilation, rubric grading, research synthesis; a fast model (Sonnet/Haiku-class) for
   tutor turns, item generation, extraction; small local/embedded models for on-device offline
   tutoring and embeddings. Every AI call is **evaluated** (golden sets, LLM-judge + human
   spot-check) and **cost-attributed** to a learner-minute.
7. **Clients:** Next.js web (learner + org + parent), Flutter mobile (offline-first, voice-first).
8. **Multi-tenant** from day one: `Personal` · `Institution` (school/university) · `Enterprise`
   (L&D) · `Coach` (1:many mentor). Row-level isolation + per-tenant graph overlays.
9. **Privacy:** the Twin is the most intimate data object in the product. Learner-owned,
   exportable, deletable, never sold, never used to train shared models without explicit,
   revocable, per-purpose consent. Model of every user is *theirs*.

---

## 6. Doc set (this blueprint)

`00` Index · `01` Vision & Category · `02` The Ten Inventions · `03` Personas & Jobs ·
`04` Journeys · `05` Functional Reqs · `06` Non-functional Reqs · `07` System Architecture ·
`08` Cognitive Twin · `09` Knowledge Graph · `10` Curriculum Compiler & Scheduler ·
`11` Tutor & Pedagogy · `12` Assessment & Proof-of-Mastery · `13` Reality Loop ·
`14` News Engine · `15` Research Engine · `16` Project Engine · `17` Career Engine ·
`18` Motivation & Accountability · `19` Analytics · `20` AI Architecture (agents, RAG, evals) ·
`21` Data Model & API · `22` Security, Privacy, Multi-tenancy · `23` UX (web, mobile, offline, i18n) ·
`24` Monetization & GTM · `25` Roadmap.

---

## 6b. CANONIZED — findings that emerged during the blueprint

These were not in the original brief. They emerged from writing the engines and are now
**canon**, because each one makes the system *more honest* than it was designed to be.

### C-1 — The Decay Wall (carrying capacity)

> **⚠️ CORRECTED IN M4.3 — by building the simulation instead of asserting it.**
>
> The original claim below was that the wall is a **divergence**: that below some minutes/day,
> decay removes mastery faster than a learner can add it and the forward simulation *never
> converges*.
>
> **That is false, and the simulation says so.** Spaced repetition forbids divergence: every
> successful retrieval pushes the next one exponentially further away, so a concept's long-run
> maintenance cost falls towards zero and the total bill grows *sub-linearly*. It always
> converges. This is not a modelling artefact — it is why real spaced-repetition users hold tens
> of thousands of items on twenty minutes a day. A model in which they could not would be a model
> of despair, not of memory.
>
> **What actually happens is worse in a quieter way.** At 20 min/day the 180-concept objective is
> reached — *three and a half years later*, with the learner spending most of every session
> treading water. Measured against an eighteen-month horizon, they hold **85 of 180 concepts** and
> are still climbing.
>
> So **the wall is a horizon, not a divergence.** The refusal stands, unchanged and just as
> important. Only the reason has been corrected, and it is now the true one.
>
> The real numbers, simulated (180 concepts, 18-month horizon):
>
> | Minutes/day | Verdict |
> |---|---|
> | 20 | **No.** In 18 months you would hold 85 of 180. |
> | 30 | **No.** In 18 months you would hold 127 of 180. |
> | 45 | Yes — 17 months. |
> | 60 | Yes — 12 months. |
> | 90 | Yes — 8 months. |

There exists a daily-minutes floor below which an Objective is **unreachable inside any horizon a
human would accept** — not "slow", *unreachable in practice*. **PRAXIS must say so, out loud, and
narrow the Objective rather than sell a plan it does not believe.** Every other product in this
industry would happily take the money.
> *"At 20 minutes a day you will not become an AI Engineer inside two years — you would hold about
> half of it, and spend most of your time keeping what you have. What you CAN become in that time
> is a competent ML practitioner in PyTorch, by week 40. Shall I compile that instead — or can you
> find 45 minutes?"*
→ `10-curriculum-compiler.md`

### C-2 — Reality scores the sources, too (E8 × E10)
The Reality Engine resolves predictions against ground truth. Point it at *pundits, analysts,
authors and outlets* and it produces **Brier scores for sources**. PRAXIS therefore becomes the
only retrieval system in existence whose source ranking is trained on **whether the source
turned out to be right** — not on citations, backlinks, or authority. A trusted source here is
one that *predicted correctly*, measured, over time.
→ `15-research-engine.md`, `13-reality-loop.md`

### C-3 — Contested news becomes a Reality Loop (E9 → E8)
When the News Engine finds a claim marked `CONTESTED` between credible sources, it does not
average them or pick one. It **converts the disagreement into a Reality Loop** and lets the
world settle it — grading the learner *and* the sources.
→ `14-news-engine.md`

### C-4 — The Evidence Ceiling
LLM-graded evidence (`EXPLAIN`, rubric-scored open response) can raise belief but is
**structurally incapable of crossing the HOLD threshold on its own**. Only evidence the model
cannot fabricate — `APPLY` on a never-seen item, `BUILD` that actually runs, `PREDICT` that
reality resolved — can push a concept into HELD. This is how a hallucinating grader can never
manufacture mastery.
→ `08-cognitive-twin.md`

### C-5 — The tutor never holds the answer key
The Answer Budget is not enforced by asking the model nicely. **The answer is never placed in
the tutor's context.** The tutor calls a `check_claim` oracle. A jailbreak retrieves nothing,
because there is nothing there to retrieve.
→ `11-tutor-and-pedagogy.md`, `20-ai-architecture.md`

### C-6 — Cheating shows up in calibration, not in scores
A learner who outsources their answers gets good scores and **catastrophic calibration** — they
cannot predict their own correctness, because they never produced it. We therefore do not need
to *block* cheating. We need only to *measure* it, discount it, and let the Ledger record the
assurance level honestly.
→ `12-assessment-and-proof-of-mastery.md`, `22-security-privacy-multitenancy.md`

---

## 7. House style for every doc

- Structured sections, minimal prose. Tables > paragraphs.
- At least one **Mermaid diagram** per doc.
- Concrete: real examples (stock market, AI engineering, medicine, law), real numbers,
  real schemas, real prompts, real formulas. No hand-waving.
- Every feature states **which Invention (I-1..I-10) it serves**. If none — delete it.
- Call out **what is genuinely novel** vs. what is table stakes.
