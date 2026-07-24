# Reading English Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a third domain pack — "Reading English", the receiving-side twin of `english-everyday` — as a validated YAML pack, an onboarding card, and a CI loader test, with **zero engine changes**.

**Architecture:** A domain pack is a single `pack.yaml` of concepts (slug, summary, difficulty/minutes priors, `requires`/`contrasts` edges, named misconceptions, and a proof-of-mastery contract per concept). The Graph's `PackValidator`/`PoMContract` gate it; nothing else changes. The diagnostic, item generator, tutor, Twin and memory already operate on any validated pack. We add the YAML, one entry in the web onboarding catalog, and one xUnit test that loads the shipped YAML from disk and asserts it passes the gate — exactly as `EnglishPackTests` does for `english-everyday`.

**Tech Stack:** YAML (pack) · C# / xUnit / .NET 10 (loader test) · TypeScript / Vitest (onboarding card + tests) · `dotnet run -- --load-pack` (dev DB load) · `scripts/verify.ps1` (the gate).

## Global Constraints

- **No engine changes.** Only three artifacts are touched: `packs/english-reading/pack.yaml` (new), `web/lib/onboarding.ts` (+1 catalog entry), and a new test file in the Graph test project. No `.cs` under `src/`, no AI-plane Python, no web components.
- **American spelling** throughout summaries and prose.
- **15 concepts, 3 regions** — not exhaustive; the shortest honest receiving-side v1.
- **Apply is the floor-carrier on every concept** (its `max` ≥ `hold`). No `Build`/`Predict` (the Reality Loop is code-only in Phase 0; a comprehension answer cannot run against hidden tests). No `Recall`.
- **Ceiling-bound evidence** (`Explain` ≤ 0.20, `Critique` ≤ 0.15) accelerates but can never cross `hold` alone — enforced by `PoMContract.Create` (CANON C-4).
- **Misconception keys** are global, kebab-case (`[a-z0-9-]`, no leading/trailing hyphen), ≤ 64 chars — `PackValidator` enforces all three. There are exactly **8** across the pack.
- **`requires`/`contrasts` edges stay inside this pack.** Cross-pack edges are not supported (one Objective compiles one path). Mirror concepts to the writing pack are noted in prose only.
- **Listening is out of scope** — deferred to M17 (needs audio infrastructure). The pack is named "Reading English", not "Comprehension", so listening can be its sibling.
- Spec of record: `docs/superpowers/specs/2026-07-24-english-reading-pack-design.md`.

---

## File Structure

- **Create** `packs/english-reading/pack.yaml` — the 15-concept pack. One responsibility: declare the reading-comprehension graph and its proof contracts.
- **Create** `tests/Praxis.Modules.Graph.Tests/EnglishReadingPackTests.cs` — the CI gate for the shipped YAML (validator passes, 15 concepts, every concept holdable, misconception keys pinned).
- **Modify** `web/lib/onboarding.ts` — add one `OBJECTIVES` entry, `obj-reading-english`.
- **Modify** `web/lib/onboarding.test.ts` — add a `describe` block covering the new card.
- **Modify** `PLAN.md` — tick the milestone with a one-line note on what landed.

Reference (read before starting, do not modify): `packs/english-everyday/pack.yaml` (the format), `src/Modules/Praxis.Modules.Graph/Packs/PackValidator.cs` (the rules), `tests/Praxis.Modules.Graph.Tests/EnglishPackTests.cs` (the test to mirror), `src/Praxis.Shared/EvidenceType.cs` (the ceiling).

---

## Task 1: The pack YAML, gated by its CI loader test

Author the pack test-first: write the test that loads `packs/english-reading/pack.yaml` and asserts it passes the validator and holds its shape, watch it fail (no file), author the YAML until green.

**Files:**
- Create: `tests/Praxis.Modules.Graph.Tests/EnglishReadingPackTests.cs`
- Create: `packs/english-reading/pack.yaml`

**Interfaces:**
- Consumes (existing, do not change): `Praxis.Modules.Graph.Packs.PackLoader.Parse(string yaml) -> Result<PackDocument>`; `PackValidator.Validate(PackDocument) -> Result<ValidatedPack>`; `PackDocument.Concepts` (`IReadOnlyList<PackConcept>`); `PackConcept.Slug`, `.Misconceptions` (each `.Key`), `.Proof` (`.Hold`, `.Evidence` each `.Type`, `.Max`); `Praxis.Shared.EvidenceTypes.CanCrossTheHoldThreshold` (`IReadOnlySet<EvidenceType>`).
- Produces: the shipped `packs/english-reading/pack.yaml`, relied on by Task 3 (dev-DB load) and by nothing in code (it is loaded from disk at test time and at `--load-pack` time).

- [ ] **Step 1: Write the failing test**

Create `tests/Praxis.Modules.Graph.Tests/EnglishReadingPackTests.cs`. This mirrors `EnglishPackTests.cs` exactly, retargeted at `english-reading` with the counts and pinned keys for this pack:

```csharp
using Praxis.Modules.Graph.Packs;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

/// The third shipped pack — reading comprehension — held to the same CI standard as the first
/// two. A wrong prerequisite does not throw; it walks a learner into a wall weeks from now and
/// they blame themselves. So the reading pack is validated in CI exactly the way the
/// ai-engineering and everyday-English packs are.
public sealed class EnglishReadingPackTests
{
    [Fact]
    public void The_reading_pack_passes_the_validator()
    {
        var result = PackValidator.Validate(Document());

        Assert.True(result.IsSuccess, string.Join("\n", result.Errors));
    }

    [Fact]
    public void It_has_fifteen_concepts()
        => Assert.Equal(15, Document().Concepts.Count);

    [Fact]
    public void Every_concept_can_actually_be_held()
    {
        // The mirror of the Evidence Ceiling: unfakeable evidence must be sufficient on its own,
        // or a learner could work forever and never finish. For reading, the unfakeable evidence
        // is a never-seen passage answered against a fixed key.
        foreach (var concept in Document().Concepts)
        {
            var crossable = concept.Proof!.Evidence
                .Where(e => EvidenceTypes.CanCrossTheHoldThreshold.Contains(e.Type))
                .Sum(e => e.Max);

            Assert.True(
                crossable >= concept.Proof.Hold,
                $"'{concept.Slug}' cannot be held by unfakeable evidence alone "
                + $"({crossable:F2} against a threshold of {concept.Proof.Hold:F2}).");
        }
    }

    [Fact]
    public void The_reading_habit_misconceptions_are_declared_by_name()
    {
        // The pack hunts reading-habit transfer errors by name — the same distractor traps a
        // Nepali reader falls into. If these keys drift, the item generator loses its targets.
        var keys = Document().Concepts
            .SelectMany(c => c.Misconceptions.Select(m => m.Key)).ToHashSet();

        Assert.Contains("keyword-match-wins", keys);
        Assert.Contains("literal-only", keys);
        Assert.Contains("nearest-noun-reference", keys);
        Assert.Contains("every-word-or-fail", keys);
        Assert.Equal(8, keys.Count);
    }

    private static string Yaml()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "packs")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        return File.ReadAllText(Path.Combine(root!.FullName, "packs", "english-reading", "pack.yaml"));
    }

    private static PackDocument Document()
    {
        var parsed = PackLoader.Parse(Yaml());
        Assert.True(parsed.IsSuccess, parsed.Message);
        return parsed.Value;
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Praxis.Modules.Graph.Tests --filter FullyQualifiedName~EnglishReadingPackTests`
Expected: FAIL. `Yaml()` cannot find `packs/english-reading/pack.yaml` (the `File.ReadAllText` throws `DirectoryNotFoundException`/`FileNotFoundException`), so every test in the class errors.

- [ ] **Step 3: Author the pack YAML**

Create `packs/english-reading/pack.yaml` with the exact content below. It is the complete pack — 15 concepts, 8 misconceptions, a DAG of `requires` edges, `contrasts` on the confusable pair, and an Apply-floor proof contract on every concept (Apply `max` = `hold`; one ceiling evidence per concept: `Critique` 0.15 where a misconception is declared, `Explain` 0.20 otherwise).

```yaml
# PRAXIS — domain pack: Reading English
#
# Objective: "Understand everyday and work English at reading speed — get the point, catch what
# is implied, and trust the right answer over the one that merely repeats the words."
#
# One objective. The receiving-side twin of english-everyday.
#
# ── Who this pack is authored for ────────────────────────────────────────────────
#
# An intermediate learner whose first language is Nepali. The misconceptions below are not
# grammar-transfer errors — they are READING-HABIT errors: the word-by-word decoding, the
# dictionary reflex, the keyword-match trap, the nearest-noun guess. Each is a specific, nameable
# habit that quietly wrecks comprehension, and each is declared so the item generator can hunt it
# (M14.2) and the Twin can watch it die.
#
# Listening is deliberately absent. It needs audio PRAXIS does not have yet; rather than fake it
# with transcripts (the pretending C-1 exists to stop), it is a named follow-up, M17 — Listening
# English. This pack is "Reading English" so that one can be its honest sibling.
#
# ── How to read a concept ────────────────────────────────────────────────────────
#
#   difficulty  IRT logit prior, -3 (trivial) to +3 (brutal), for an INTERMEDIATE reader —
#               replaced by measurement once the cohort responds.
#   minutes     Focused work for a median learner to first produce evidence. A prior.
#   requires    The ONLY ordering claim in this file. Edges stay inside this pack.
#   contrasts   The confusable pair — where the Assessment engine hunts distractors.
#   proof       CEILING: model-graded evidence (explain, critique) can never sum to hold.
#               FLOOR: apply alone is always sufficient. Apply, for reading, means: a NEVER-SEEN
#               passage answered against a FIXED KEY (multiple choice, true/false/not-given, span
#               selection, ordering) — deterministic, unfakeable, and the key is never in the
#               tutor's context (C-5). No build/predict; a comprehension answer cannot run against
#               hidden tests. No recall; there is nothing here to memorize.

domain: english-reading
version: 1
note: >
  Third shipped pack. 15 concepts on the shortest honest path to understanding everyday and work
  English at reading speed, authored for intermediate readers transferring from Nepali.
  Difficulties and durations are priors awaiting cohort data. American spelling throughout.

concepts:

  # ── Region A · Decoding the sentence: the obstacles that stop the eye ───────

  - slug: long-noun-phrases
    title: "Long noun phrases: find the head"
    summary: >
      English stacks nouns into towers — "customer service response time targets" — and the word
      it is really about is the last one, with everything before it describing it. A Nepali reader
      trained on postpositions reaches for the first noun and loses the thread. Mastery is finding
      the head of any stack fast, so a dense work sentence stops being a wall.
    difficulty: 0.2
    minutes: 30
    misconceptions:
      - key: head-is-first-noun
        statement: >
          I think the first noun in a long string is what the phrase is about, so "response time"
          is the point of "customer service response time targets" — I read the tower left to right
          and stop at the first thing that makes sense.
    proof:
      hold: 0.75
      evidence:
        - { type: Critique, min-score: 0.7, max: 0.15 }
        - { type: Apply, min-score: 0.8, max: 0.75 }

  - slug: embedded-clauses
    title: "Embedded clauses: the subject and verb split apart"
    summary: >
      English drops a whole clause between a subject and its verb — "the report that the manager
      you emailed asked for is late" — and the reader who cannot bracket the middle loses who did
      what. Mastery is holding the main frame while a relative or subordinate clause interrupts it,
      and knowing the interruption ended.
    difficulty: 0.6
    minutes: 35
    requires: [long-noun-phrases]
    proof:
      hold: 0.75
      evidence:
        - { type: Explain, min-score: 0.7, max: 0.20 }
        - { type: Apply, min-score: 0.8, max: 0.75 }

  - slug: reference-tracking
    title: "Reference: what it, they, this point back to"
    summary: >
      "It", "they", "this", "the former" — English leans hard on words that stand in for something
      said earlier, sometimes paragraphs back. Mastery is tracing every reference to the right
      antecedent, not the nearest noun, because the nearest noun is the trap the sentence was built
      around.
    difficulty: 0.4
    minutes: 35
    requires: [embedded-clauses]
    misconceptions:
      - key: nearest-noun-reference
        statement: >
          I think "it", "they" and "this" refer to the closest noun before them, so I grab whatever
          word is nearest and move on — the writer put it there, so that must be what they mean.
    proof:
      hold: 0.78
      evidence:
        - { type: Critique, min-score: 0.7, max: 0.15 }
        - { type: Apply, min-score: 0.8, max: 0.78 }

  - slug: connectives-that-flip
    title: "Connectives that flip the meaning"
    summary: >
      "However", "although", "whereas", "nonetheless" — small words that reverse the sentence that
      follows them. Skip one and you read the opposite of what was written. Mastery is treating a
      connective as a turn signal: it tells you which way the meaning is about to go before you get
      there.
    difficulty: 0.5
    minutes: 30
    requires: [embedded-clauses]
    misconceptions:
      - key: connectives-are-filler
        statement: >
          I think words like "however" and "although" are just polish between the real facts, so I
          skim past them to the nouns and verbs — the important information is in the content words,
          not the little joining words.
    proof:
      hold: 0.78
      evidence:
        - { type: Critique, min-score: 0.7, max: 0.15 }
        - { type: Apply, min-score: 0.8, max: 0.78 }

  - slug: phrasal-verbs-in-context
    title: "Phrasal verbs: read them, don't translate them"
    summary: >
      "Put off", "look into", "run out of" — verb plus particle, meaning something no dictionary of
      the parts predicts. A reader who translates the words gets nonsense. Mastery is inferring the
      whole unit's meaning from the sentence around it, the same way a fluent reader never notices
      they are doing it.
    difficulty: 0.3
    minutes: 30
    requires: [embedded-clauses]
    proof:
      hold: 0.75
      evidence:
        - { type: Explain, min-score: 0.7, max: 0.20 }
        - { type: Apply, min-score: 0.8, max: 0.75 }

  - slug: what-the-article-signals
    title: "What a and the tell a reader"
    summary: >
      On the writing side you choose articles; on the reading side they are signals. "a client"
      means new — you have not met them yet. "the client" means you should already know which one,
      from earlier in the text or from shared context. Mastery is reading articles as the writer's
      quiet instructions about what is new and what is assumed. (Mirror of the writing pack's
      a-an-first-mention, from the other direction.)
    difficulty: 0.1
    minutes: 35
    requires: [embedded-clauses]
    proof:
      hold: 0.75
      evidence:
        - { type: Explain, min-score: 0.7, max: 0.20 }
        - { type: Apply, min-score: 0.8, max: 0.75 }

  # ── Region B · Getting the meaning: the strategies over the whole text ──────

  - slug: gist-vs-detail
    title: "Gist vs detail: match effort to purpose"
    summary: >
      Reading is not one speed. Sometimes you want the point of a whole email in five seconds;
      sometimes you need the one date buried in paragraph three. Mastery is choosing — skim for
      gist, scan for a fact, slow-read only where it pays — instead of grinding every text word by
      word as if all of it mattered equally.
    difficulty: -0.8
    minutes: 25
    requires: [embedded-clauses]
    misconceptions:
      - key: every-word-or-fail
        statement: >
          I think if I do not understand every word, I have not understood the passage — so I stop
          at the first unknown word and cannot move on until it is resolved, and reading stays slow
          and exhausting.
    proof:
      hold: 0.75
      evidence:
        - { type: Critique, min-score: 0.7, max: 0.15 }
        - { type: Apply, min-score: 0.8, max: 0.75 }

  - slug: main-idea
    title: "Main idea: the point, not a line of it"
    summary: >
      The main idea is the sentence that would survive if you cut the passage to one line — it
      covers the whole thing, not one vivid detail. Mastery is telling the point from its support,
      and refusing the answer that merely repeats words you can see, because repeating the text is
      not the same as being its point.
    difficulty: 0.4
    minutes: 40
    requires: [gist-vs-detail, what-the-article-signals]
    contrasts: [gist-vs-detail]
    misconceptions:
      - key: keyword-match-wins
        statement: >
          I think the right answer is the option that shares the most words with the passage, so I
          pick whichever choice repeats phrases I recognize — if the words match, it must be what
          the text says.
    proof:
      hold: 0.78
      evidence:
        - { type: Critique, min-score: 0.7, max: 0.15 }
        - { type: Apply, min-score: 0.8, max: 0.78 }

  - slug: inference
    title: "Inference: the safe step past what is printed"
    summary: >
      Writers imply more than they state. Inference is the conclusion the text supports without
      saying — and the discipline is its two edges: the safe step the words license, versus the
      overreach they do not. Mastery is drawing what must be true, refusing what merely could be,
      and never mistaking "printed literally" for "the whole meaning".
    difficulty: 1.5
    minutes: 45
    requires: [main-idea, reference-tracking, connectives-that-flip]
    misconceptions:
      - key: literal-only
        statement: >
          I think only what is printed on the page counts as true, so a question about what the
          writer implies feels like a trick — if it does not say it in words, I cannot conclude it.
    proof:
      hold: 0.80
      evidence:
        - { type: Critique, min-score: 0.7, max: 0.15 }
        - { type: Apply, min-score: 0.8, max: 0.80 }

  - slug: authors-stance
    title: "Author's stance: fact, opinion, and which side"
    summary: >
      Behind most real writing is a position — for, against, or genuinely neutral — and a mix of
      fact and opinion the reader has to sort. Mastery is hearing the writer's stance and telling a
      reported fact from a confident claim, because a sentence written with certainty is not
      thereby true.
    difficulty: 1.4
    minutes: 45
    requires: [inference]
    misconceptions:
      - key: fact-and-opinion-same
        statement: >
          I think if something is written confidently and in print, it is a fact — I do not
          separate what the writer is claiming from what is actually established, so opinion reads
          to me as truth.
    proof:
      hold: 0.80
      evidence:
        - { type: Critique, min-score: 0.7, max: 0.15 }
        - { type: Apply, min-score: 0.8, max: 0.80 }

  - slug: vocab-from-context
    title: "Vocabulary from context: no dictionary"
    summary: >
      A fluent reader meets unknown words constantly and rarely stops — they infer a workable
      meaning from the neighbors and read on, sharpening it over later sightings. Mastery is doing
      the same: a good-enough guess from context beats a dictionary lookup that breaks your flow
      and is forgotten by the next paragraph.
    difficulty: 0.0
    minutes: 30
    requires: [gist-vs-detail]
    misconceptions:
      - key: dictionary-reflex
        statement: >
          I think every unknown word means stop and look it up, so I break my reading for each one
          — I cannot trust a meaning I only guessed, so I check, and by the time I am back I have
          lost the thread.
    proof:
      hold: 0.75
      evidence:
        - { type: Critique, min-score: 0.7, max: 0.15 }
        - { type: Apply, min-score: 0.8, max: 0.75 }

  - slug: reading-tone-register
    title: "Reading tone and register"
    summary: >
      The same request reads warm or curt depending on word choice, and the register tells you how
      the writer sees the relationship and what they really want. Mastery is reading tone off the
      page — formal or friendly, patient or impatient — and using it to understand intent, not just
      content. (Mirror of the writing pack's formal-vs-informal, from the reading side.)
    difficulty: 0.8
    minutes: 35
    requires: [authors-stance]
    proof:
      hold: 0.78
      evidence:
        - { type: Explain, min-score: 0.7, max: 0.20 }
        - { type: Apply, min-score: 0.8, max: 0.78 }

  # ── Region C · Real-world receiving: where it pays off ─────────────────────

  - slug: reading-a-work-email
    title: "Reading a work email: what is actually being asked"
    summary: >
      A real work email hides one thing that matters — the ask — inside greetings, context, and
      politeness. Mastery is extracting it fast: what action is required of you, by when, and what
      is only background, so you answer the email that was sent rather than the one you skimmed.
    difficulty: 0.3
    minutes: 30
    requires: [main-idea]
    proof:
      hold: 0.78
      evidence:
        - { type: Explain, min-score: 0.7, max: 0.20 }
        - { type: Apply, min-score: 0.8, max: 0.78 }

  - slug: following-instructions
    title: "Following instructions: steps, conditions, order"
    summary: >
      Instructions in prose carry logic — do this before that, but only if this other thing is
      true. "If X, then Y; otherwise Z" hides in plain sentences. Mastery is reading procedure and
      condition out of ordinary text and executing in the right order, catching the "unless" and
      the "before" that change everything.
    difficulty: 0.2
    minutes: 30
    requires: [connectives-that-flip]
    proof:
      hold: 0.78
      evidence:
        - { type: Explain, min-score: 0.7, max: 0.20 }
        - { type: Apply, min-score: 0.8, max: 0.78 }

  - slug: news-vs-opinion
    title: "News vs opinion: reported fact vs argued claim"
    summary: >
      A news report and an opinion piece can look identical and do opposite things — one relays
      what happened, the other argues you into a view. Mastery is telling them apart on sight, so
      you read a claim as a claim to be weighed and a report as a fact to be checked, and never
      swallow an argument because it was printed like news.
    difficulty: 1.0
    minutes: 40
    requires: [inference, authors-stance]
    proof:
      hold: 0.80
      evidence:
        - { type: Explain, min-score: 0.7, max: 0.20 }
        - { type: Apply, min-score: 0.8, max: 0.80 }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Praxis.Modules.Graph.Tests --filter FullyQualifiedName~EnglishReadingPackTests`
Expected: PASS — all four tests green (validator passes, 15 concepts, every concept holdable, 8 misconception keys with the four pinned ones present).

If `The_reading_pack_passes_the_validator` fails, read the joined `result.Errors` in the assertion message — it names the offending slug and the exact rule (dangling edge, cycle, orphan, ceiling violation, bad key). Fix the YAML, not the validator.

- [ ] **Step 5: Commit**

```bash
git add packs/english-reading/pack.yaml tests/Praxis.Modules.Graph.Tests/EnglishReadingPackTests.cs
git commit -m "Reading English pack 1/2 - fifteen concepts on the shortest path to reading comprehension, gated in CI (M16)"
```

(Use the milestone id your PLAN.md assigns; this plan assumes **M16**.)

---

## Task 2: The onboarding card

Add "Reading English" to the web catalog, test-first.

**Files:**
- Modify: `web/lib/onboarding.test.ts`
- Modify: `web/lib/onboarding.ts:38-87` (the `OBJECTIVES` array)

**Interfaces:**
- Consumes (existing, do not change): `OBJECTIVES` (`readonly Objective[]`), `searchObjectives(query: string)`, the `Objective` type (`id`, `title`, `role`, `cost`, `difficulty`, `keywords`).
- Produces: an `OBJECTIVES` entry with `id: "obj-reading-english"`, findable by `searchObjectives("reading")` and `searchObjectives("comprehension")`.

- [ ] **Step 1: Write the failing test**

In `web/lib/onboarding.test.ts`, append a new `describe` block after the `everyday english objective` block (after line 78):

```typescript
describe("reading english objective", () => {
  it('search finds it by "reading" and by "comprehension"', () => {
    expect(searchObjectives("reading").map((o) => o.id)).toContain("obj-reading-english");
    expect(searchObjectives("comprehension").map((o) => o.id)).toContain("obj-reading-english");
  });

  it("carries an honest cost line, not a brochure", () => {
    const reading = OBJECTIVES.find((o) => o.id === "obj-reading-english");
    expect(reading?.difficulty).toBe("Serious");
    expect(reading?.cost.length).toBeGreaterThan(20);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd web && npm test -- onboarding`
Expected: FAIL — `search finds it by "reading"...` fails (`toContain("obj-reading-english")` on an array that lacks it) and `carries an honest cost line...` fails (`reading` is `undefined`).

- [ ] **Step 3: Add the catalog entry**

In `web/lib/onboarding.ts`, add this object to the `OBJECTIVES` array immediately after the `obj-everyday-english` entry (after its closing `},` on line 54), before `obj-radiologist`:

```typescript
  {
    id: "obj-reading-english",
    title: "Reading English",
    role: "Understand everyday and work English at reading speed — get the point, catch what's implied, and trust the right answer over the one that just repeats the words. The receiving side of fluency.",
    cost: "Months of passages you only half understand. The habit of stopping to look up every word is the first thing that has to die.",
    difficulty: "Serious",
    keywords: ["reading", "comprehension", "understanding", "english", "inference", "skimming", "esl", "work email", "language"],
  },
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd web && npm test -- onboarding`
Expected: PASS — the new block is green, and the existing `searchObjectives("")` length test still passes (it reads `OBJECTIVES.length`, so it self-adjusts).

- [ ] **Step 5: Commit**

```bash
git add web/lib/onboarding.ts web/lib/onboarding.test.ts
git commit -m "Reading English pack 2/2 - the Reading English card joins the catalog (M16)"
```

---

## Task 3: Load into the dev DB, run the full gate, tick PLAN.md

Prove the pack loads into a real database and the whole repo gate is green, then record it.

**Files:**
- Modify: `PLAN.md` (append the milestone tick)

**Interfaces:**
- Consumes: `dotnet run -- --load-pack <path>` (existing CLI in `src/Praxis.Api/Program.cs:178`); `scripts/verify.ps1` (the three-stack gate).
- Produces: nothing in code — a loaded dev DB and a ticked plan.

- [ ] **Step 1: Load the pack into the dev database**

Ensure local Postgres is up on port 5455 (`netstat -ano | Select-String ":5455\s.*LISTENING"`). Then:

Run: `dotnet run --project src/Praxis.Api -- --load-pack packs/english-reading/pack.yaml`
Expected: exits 0, logs the concept/edge/misconception counts for `english-reading` (15 concepts, 8 misconceptions), and does not error. The dev DB now carries all three packs.

If it logs a validation failure, it is the same gate as Task 1 — fix the YAML and re-run Task 1's test first.

- [ ] **Step 2: Run the full gate**

Run: `pwsh scripts/verify.ps1`
Expected: PASSED — build + test + lint across all three stacks. The .NET test count rises by 4 (the new `EnglishReadingPackTests`); web rises by 2 (the new onboarding block); AI is untouched. No stage may be skipped.

- [ ] **Step 3: Tick PLAN.md**

Append a milestone section to `PLAN.md` mirroring the M15 entry's style. One box, one honest note on what landed:

```markdown
## M16 — Reading English (the third pack)

> The receiving-side twin of the everyday-English pack: reading comprehension for the same
> Nepali-transfer cohort, with **zero engine changes**. Spec:
> `docs/superpowers/specs/2026-07-24-english-reading-pack-design.md` · plan:
> `docs/superpowers/plans/2026-07-24-english-reading-pack.md`.

- [x] **M16.1** The pack, the card, and the CI gate that holds it.
      → `packs/english-reading/pack.yaml`: 15 concepts across 3 regions (decode the sentence,
      get the meaning, real-world receiving), a DAG of requires edges, and **8 reading-habit
      misconceptions declared by name** (keyword-match-wins, literal-only, nearest-noun-reference,
      every-word-or-fail, dictionary-reflex, connectives-are-filler, fact-and-opinion-same,
      head-is-first-noun) so the generator hunts them and the Twin watches them die. Apply carries
      the floor on every contract — for reading, a never-seen passage answered against a fixed key
      the tutor never sees (C-5); no Build/Predict/Recall. `EnglishReadingPackTests` holds the
      shipped YAML to the pack standard in CI (validator gate, 15-count, every-concept-holdable,
      misconception keys pinned). "Reading English" card in `web/lib/onboarding.ts` (+2 web tests).
      Listening deferred to M17 (needs audio). Dev DB now carries three packs. Full gate PASSED.
```

- [ ] **Step 4: Commit**

```bash
git add PLAN.md
git commit -m "Reading English pack - loaded, gate green, on the record (M16)"
```

---

## Self-Review (completed by plan author)

**Spec coverage:**
- Goal / receiving-side twin → Task 1 (pack), Task 2 (card).
- 15 concepts, 3 regions, exact `requires` spine and cross-region edges → Task 1 YAML (verified against spec's map: `embedded-clauses`→`gist-vs-detail`, `reference-tracking`/`connectives-that-flip`→`inference`, `what-the-article-signals`→`main-idea`, `main-idea`→`reading-a-work-email`, `connectives-that-flip`→`following-instructions`, `inference`+`authors-stance`→`news-vs-opinion`). ✔
- 8 misconceptions on their home concepts → Task 1 YAML + pinned in the test. ✔
- Apply-floor everywhere, one ceiling evidence each, holds 0.75–0.80, no Build/Predict/Recall → Task 1 YAML; `Every_concept_can_actually_be_held` enforces the floor, `PoMContract.Create` (via `PackValidator`) enforces the ceiling. ✔
- `contrasts` on the confusable pair → `main-idea contrasts [gist-vs-detail]`. ✔
- Edges stay in-pack; mirror concepts noted in prose only → `what-the-article-signals`, `reading-tone-register` summaries carry the prose note; no cross-pack edges exist. ✔
- Onboarding card `obj-reading-english`, Serious, keyword set → Task 2. ✔
- Loader test mirroring `EnglishPackTests` → Task 1. ✔
- Listening deferred to M17, named not silent → pack header comment + PLAN.md note. ✔
- Everyday + work material, no academic/technical corpus → reflected in concept choice (`reading-a-work-email`, `news-vs-opinion`); passages are generated at runtime, so nothing to author here. ✔
- Full gate green + dev-DB load → Task 3. ✔

**Placeholder scan:** none — every step carries exact file paths, complete YAML/C#/TS, exact commands and expected output.

**Type/name consistency:** slugs referenced in `requires`/`contrasts` all exist in the concept list (long-noun-phrases, embedded-clauses, reference-tracking, connectives-that-flip, phrasal-verbs-in-context, what-the-article-signals, gist-vs-detail, main-idea, inference, authors-stance, vocab-from-context, reading-tone-register, reading-a-work-email, following-instructions, news-vs-opinion — 15). No slug requires itself. Graph is acyclic (every edge points to an earlier-authored concept). No orphan (every concept is a source or target of at least one edge; `long-noun-phrases` is required by `embedded-clauses`). 8 distinct misconception keys, each ≤ 64 chars and kebab-case. Test method names and pinned keys match the YAML.
