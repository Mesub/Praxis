# Everyday English Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship PRAXIS's second domain pack — Everyday English, Nepali-tuned — plus its onboarding card, with the shipped file held to the same test standard as ai-engineering.

**Architecture:** Pure data + one static-array entry. No engine changes. The pack is gated by `PackValidator`/`PoMContract.Create` at load; a new `EnglishPackTests` mirrors `SeedPackTests` so the shipped YAML is tested in CI. The dev wiring that makes packs loadable outside tests (`--load-pack`, Next dev proxy) is committed as part of this work.

**Tech Stack:** YAML (pack), C#/xUnit (loader tests), TypeScript/vitest (onboarding), PowerShell gate.

## Global Constraints

- Misconception keys: global-unique across the pack, kebab-case `[a-z0-9-]`, ≤64 chars (PackValidator).
- Contracts: per type one clause; Σ max(Apply,Build,Predict) ≥ hold; Σ max(Recall,Explain,Critique) < hold; hold ∈ (0,1].
- Only Apply carries the floor in this pack (Reality Loop is code-only in Phase 0 — no Build/Predict for prose).
- Every concept connected (no orphans), `requires` acyclic, no dangling slugs, no self-edges.
- American spelling in all pack prose. Difficulty −3..+3 (priors), minutes = median-learner focused time.
- Spec source of truth: `docs/superpowers/specs/2026-07-19-english-everyday-pack-design.md`.

---

### Task 1: Onboarding card for Everyday English

**Files:**
- Modify: `web/lib/onboarding.ts` (OBJECTIVES array, after obj-ai-engineer)
- Test: `web/lib/onboarding.test.ts`

**Interfaces:**
- Produces: `OBJECTIVES` entry `{ id: "obj-everyday-english", title: "Everyday English", difficulty: "Serious" }` — Task 3's manual browser check looks for this card by title.

- [ ] **Step 1: Write the failing test** — append to `web/lib/onboarding.test.ts`:

```ts
describe("everyday english objective", () => {
  it('search finds it by "english" and by "grammar"', () => {
    expect(searchObjectives("english").map((o) => o.id)).toContain("obj-everyday-english");
    expect(searchObjectives("grammar").map((o) => o.id)).toContain("obj-everyday-english");
  });

  it("carries an honest cost line, not a brochure", () => {
    const english = OBJECTIVES.find((o) => o.id === "obj-everyday-english");
    expect(english?.difficulty).toBe("Serious");
    expect(english?.cost.length).toBeGreaterThan(20);
  });
});
```

(Match the file's existing import style; add `OBJECTIVES` to the import if not present.)

- [ ] **Step 2: Run to verify it fails** — `cd web; npx vitest run lib/onboarding.test.ts` → FAIL (card absent).
- [ ] **Step 3: Add the card** to `OBJECTIVES` in `web/lib/onboarding.ts`:

```ts
{
  id: "obj-everyday-english",
  title: "Everyday English",
  role: "Write clear, natural everyday English — messages, plans, opinions — and kill the errors your first language plants. Text first: writing accuracy is the fuel speaking runs on.",
  cost: "Months of honest reps. You will find out what you only think you know.",
  difficulty: "Serious",
  keywords: ["english", "grammar", "writing", "vocabulary", "articles", "tenses", "fluency", "esl", "language"],
},
```

- [ ] **Step 4: Run to verify green** — `npx vitest run lib/onboarding.test.ts` → PASS; then full `npx vitest run` → all green.
- [ ] **Step 5: Commit** — `git add web/lib/onboarding.ts web/lib/onboarding.test.ts; git commit -m "English pack 1/3 - the Everyday English card joins the catalog"`.

### Task 2: The pack, gated red-then-green

**Files:**
- Create: `packs/english-everyday/pack.yaml`
- Test: `tests/Praxis.Modules.Graph.Tests/EnglishPackTests.cs`

**Interfaces:**
- Consumes: `PackLoader.Parse(string)`, `PackValidator.Validate(PackDocument)` (existing).
- Produces: the shipped file `packs/english-everyday/pack.yaml`, domain `english-everyday`, version 1, 31 concepts — Task 3 loads exactly this path.

- [ ] **Step 1: Write the failing test** — `EnglishPackTests.cs`, mirroring `SeedPackTests` (same root-walk, `packs/english-everyday/pack.yaml`):

```csharp
using Praxis.Modules.Graph.Packs;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Tests;

/// The second shipped pack, held to the same standard as the first: a wrong prerequisite
/// does not throw — it walks a learner into a wall weeks from now, and they blame themselves.
public sealed class EnglishPackTests
{
    [Fact]
    public void The_english_pack_passes_the_validator()
    {
        var result = PackValidator.Validate(Document());
        Assert.True(result.IsSuccess, string.Join("\n", result.Errors));
    }

    [Fact]
    public void It_has_thirty_one_concepts()
        => Assert.Equal(31, Document().Concepts.Count);

    [Fact]
    public void Every_concept_can_actually_be_held()
    {
        foreach (var concept in Document().Concepts)
        {
            var crossable = concept.Proof!.Evidence
                .Where(e => EvidenceTypes.CanCrossTheHoldThreshold.Contains(e.Type))
                .Sum(e => e.Max);

            Assert.True(
                crossable >= concept.Proof.Hold,
                $"'{concept.Slug}' cannot be held by unfakeable evidence alone.");
        }
    }

    [Fact]
    public void The_nepali_transfer_beliefs_are_declared_by_name()
    {
        var keys = Document().Concepts
            .SelectMany(c => c.Misconceptions.Select(m => m.Key)).ToHashSet();

        Assert.Contains("articles-are-decoration", keys);
        Assert.Contains("perfect-is-fancy-past", keys);
        Assert.Contains("prepositions-translate-literally", keys);
        Assert.Contains("questions-by-tone-alone", keys);
        Assert.Equal(15, keys.Count);
    }

    private static string Yaml()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "packs")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "packs", "english-everyday", "pack.yaml"));
    }

    private static PackDocument Document()
    {
        var parsed = PackLoader.Parse(Yaml());
        Assert.True(parsed.IsSuccess, parsed.Message);
        return parsed.Value;
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/Praxis.Modules.Graph.Tests --filter FullyQualifiedName~EnglishPackTests` → FAIL (file not found).
- [ ] **Step 3: Author `packs/english-everyday/pack.yaml`** — domain `english-everyday`, version 1, note, then the 31 concepts below. Contract archetypes (reuse verbatim; every one satisfies the Global Constraints arithmetic):
  - **A (vocab-flavored):** hold 0.75; Recall {min-score 0.9, max 0.15}; Apply {min-score 0.8, max 0.75}
  - **B (rule-flavored):** hold 0.80; Explain {min-score 0.7, max 0.20}; Apply {min-score 0.8, max 0.80}
  - **C (misconception-heavy):** hold 0.80; Critique {min-score 0.7, max 0.15}; Apply {min-score 0.8, max 0.80}
  - **D (writing tasks):** hold 0.80; Explain {min-score 0.7, max 0.15}; Apply {min-score 0.8, max 0.80}

  The 31 concepts (slug · diff · min · requires · contrasts · contract · misconception key):

  | # | slug | diff | min | requires | contrasts | c | misconception |
  |---|---|---|---|---|---|---|---|
  | 1 | word-order-svo | −1.2 | 30 | — | — | C | word-order-flexible |
  | 2 | subject-verb-agreement | −0.9 | 30 | word-order-svo | — | C | agreement-is-optional |
  | 3 | negation-do | −0.8 | 25 | word-order-svo | — | B | — |
  | 4 | questions-do-support | −0.5 | 35 | word-order-svo, negation-do | — | C | questions-by-tone-alone |
  | 5 | a-an-first-mention | −0.7 | 35 | word-order-svo | the-definite-reference | C | articles-are-decoration |
  | 6 | the-definite-reference | −0.2 | 45 | a-an-first-mention | a-an-first-mention | C | the-marks-importance |
  | 7 | zero-article-generalities | 0.3 | 40 | the-definite-reference | — | B | — |
  | 8 | countable-uncountable | −0.4 | 35 | word-order-svo | — | C | uncountables-take-plural |
  | 9 | quantifiers | 0.0 | 40 | countable-uncountable | — | C | much-many-interchangeable |
  | 10 | present-simple-vs-continuous | −0.6 | 40 | subject-verb-agreement | — | C | continuous-shows-politeness |
  | 11 | past-simple | −0.5 | 35 | present-simple-vs-continuous | — | C | double-past-did |
  | 12 | present-perfect-vs-past | 0.6 | 55 | past-simple | past-simple | C | perfect-is-fancy-past |
  | 13 | future-forms | 0.2 | 45 | present-simple-vs-continuous | — | C | one-future-will |
  | 14 | past-continuous | 0.1 | 35 | past-simple | past-simple | B | — |
  | 15 | in-on-at-time | −0.3 | 30 | word-order-svo | in-on-at-place | B | — |
  | 16 | in-on-at-place | −0.1 | 30 | in-on-at-time | in-on-at-time | B | — |
  | 17 | verb-preposition-pairs | 0.5 | 50 | in-on-at-place | — | C | prepositions-translate-literally |
  | 18 | gerund-vs-infinitive | 0.4 | 45 | present-simple-vs-continuous | — | C | gerund-infinitive-same |
  | 19 | phrasal-verbs-core | 0.7 | 55 | gerund-vs-infinitive, verb-preposition-pairs | — | A | — |
  | 20 | modals-ability-permission | −0.2 | 30 | negation-do | — | B | — |
  | 21 | modals-advice-obligation | 0.3 | 40 | modals-ability-permission | modals-ability-permission | B | — |
  | 22 | collocations-make-do-take-have | 0.2 | 45 | the-definite-reference | — | C | collocation-any-verb |
  | 23 | word-families | 0.4 | 40 | collocations-make-do-take-have | — | A | — |
  | 24 | nepali-english-false-friends | 0.1 | 35 | collocations-make-do-take-have | — | A | — |
  | 25 | linking-words | 0.3 | 40 | present-perfect-vs-past | — | B | — |
  | 26 | relative-clauses | 0.8 | 50 | linking-words | — | B | — |
  | 27 | conditionals-real-unreal | 1.1 | 55 | relative-clauses, past-simple | — | C | — (uses contrasts) |
  | 28 | reported-speech-basics | 1.6 | 55 | conditionals-real-unreal, past-simple | — | B | — |
  | 29 | formal-vs-informal | 0.6 | 40 | linking-words | — | C | formal-is-always-better |
  | 30 | everyday-messages | 0.5 | 50 | formal-vs-informal, verb-preposition-pairs | — | D | — |
  | 31 | short-emails | 0.9 | 55 | everyday-messages | — | D | — |

  Every concept gets a 2–4 line `summary` (American spelling) written from the Nepali-speaker's
  seat: what the rule is, why it feels wrong coming from Nepali, what mastery looks like.
  Misconception `statement`s come verbatim from the spec's table, first person.
  Also give conditionals-real-unreal `contrasts: [past-simple]` (unreal-conditional past is not
  time-past — the confusable pair distractors mine).
- [ ] **Step 4: Run to verify green** — same filter → 4/4 PASS. Then the full Graph project: `dotnet test tests/Praxis.Modules.Graph.Tests` → all green.
- [ ] **Step 5: Commit** — `git add packs/english-everyday/pack.yaml tests/Praxis.Modules.Graph.Tests/EnglishPackTests.cs; git commit -m "English pack 2/3 - thirty-one concepts on the shortest honest path to everyday written English"`.

### Task 3: Dev wiring, dev-DB load, gate, PLAN.md

**Files:**
- Modify (already edited, uncommitted): `src/Praxis.Api/Program.cs` (--load-pack), `web/next.config.ts` (dev proxy)
- Modify: `PLAN.md` (new M15 section)

**Interfaces:**
- Consumes: Task 2's shipped file path `packs/english-everyday/pack.yaml`; `dotnet run -- --load-pack <abs path>`.

- [ ] **Step 1: Load the pack into the dev DB** — `dotnet run --project src/Praxis.Api -- --load-pack D:\claude\Praxis\packs\english-everyday\pack.yaml` → log line `Pack loaded: english-everyday v1 (31 concepts, N edges)`.
- [ ] **Step 2: See it in the browser** (services already up): reload `http://localhost:3001/graph` with the auth shim → concept count rises by 31.
- [ ] **Step 3: Full gate** — `powershell -File scripts/verify.ps1` → PASSED every stage.
- [ ] **Step 4: PLAN.md** — add section `## M15 — Everyday English (second pack)` with one `- [x]` line noting pack, card, tests, and the dev wiring (`--load-pack`, dev proxy).
- [ ] **Step 5: Commit** — `git add src/Praxis.Api/Program.cs web/next.config.ts PLAN.md docs/superpowers/plans/2026-07-19-english-everyday-pack.md; git commit -m "English pack 3/3 - a pack is loadable outside tests, and the plan is on the record (M15)"`.
