# M17 — The fixed-key reading item: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A reading-comprehension item — a never-seen passage plus four choices — can be generated, banked, served, answered by selection, and marked by comparing two integers, with the correct index living only in the `praxis_oracle` database the .NET core cannot reach.

**Architecture:** The item grows a *shape* (`Format`, `Passage`, `Options`) in the `assessment` schema; the *key* (`correct_option`, and which declared wrong belief each distractor catches) grows in the oracle's own separate database on the far side of the AI-plane wire. Marking for a fixed-key item is an integer comparison inside `Oracle.check` with no model call. The tutor is handed the passage and has no field an option could arrive in.

**Tech Stack:** .NET 10 (vertical slice, FluentValidation, EF Core + Npgsql, xUnit) · Python 3.14 (FastAPI, pydantic, pytest) · Next.js 16 + TypeScript + Tailwind (vitest).

Spec: `docs/superpowers/specs/2026-07-26-fixed-key-reading-item-design.md`.

## Global Constraints

- **No answer, ever, on the core side.** No column, field, property, or wire member in `praxis` or in any `.NET` type may carry the correct option. `SchemaTests` reads `information_schema` and must stay green.
- **Vertical slice.** One use case = one file = `Command`/`Query` + `Validator` + `Handler` + `Response`. No mediator; handlers injected with `[FromServices]`. No AutoMapper.
- `Result<T>` for expected failures; exceptions only for bugs and infrastructure faults.
- One handler = one use case = one `SaveChangesAsync`.
- **Postgres on port 5455.** Connection string: `Host=localhost;Port=5455;Database=praxis;Username=praxis;Password=praxis_dev_only`.
- **Backward compatibility is not optional.** Every item already banked is `Format.Prose`, keeps model marking, and keeps its existing fingerprint byte-for-byte.
- Enum values cross the wire snake-cased: `AiPlaneClient` uses `JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)`, so `ItemFormat.MultipleChoice` ⇄ `"multiple_choice"` matches the Python `StrEnum` value exactly.
- American spelling in all learner-facing copy.
- Gate: `scripts/verify.ps1` (`.NET build`, `.NET test`, `AI lint` `uv run ruff check .`, `AI test` `uv run pytest -q`, `Web lint`, `Web test` `npm run test`, `Web build`).

## File Structure

**.NET — created**

- `src/Praxis.Shared/ItemFormat.cs` — the two shapes an item can take. Shared because both Graph (which declares it) and Assessment (which banks it) need it, exactly like `EvidenceType`.
- `src/Modules/Praxis.Modules.Tutor/ITutorContext.cs` — the seam the Tutor has been missing: what the learner is looking at, and nothing more.
- `src/Praxis.Api/Composition/TutorContext.cs` — the adapter that fills it, at the composition root.

**.NET — modified**

- `src/Modules/Praxis.Modules.Graph/Packs/PackDocument.cs` — `PackEvidence.Format`.
- `src/Modules/Praxis.Modules.Graph/Packs/PackValidator.cs` — format is legal only on Apply; multiple choice needs declared misconceptions.
- `src/Modules/Praxis.Modules.Graph/Packs/PackLoader.cs` — carry format into the contract.
- `src/Modules/Praxis.Modules.Graph/Domain/PoMContract.cs` — `AcceptedEvidence.Format`.
- `src/Modules/Praxis.Modules.Graph/Persistence/GraphDbContext.cs` + a migration.
- `src/Modules/Praxis.Modules.Assessment/Domain/BankedItem.cs` — `Format`, `Passage`, `Options`.
- `src/Modules/Praxis.Modules.Assessment/Persistence/AssessmentDbContext.cs` + a migration.
- `src/Modules/Praxis.Modules.Assessment/IConceptIntel.cs` — `ConceptIntel.ApplyFormat`.
- `src/Praxis.Api/Composition/GraphConceptIntel.cs` — read it off the contract.
- `src/Modules/Praxis.Modules.Assessment/Features/Diagnostics/EnsureConceptPool.cs` — request the format, bank the shape, set the guessing floor.
- `src/Modules/Praxis.Modules.Assessment/Features/Diagnostics/DiagnosticResponses.cs` — serve passage + options.
- `src/Modules/Praxis.Modules.Assessment/Features/Diagnostics/SubmitAnswer.cs` — accept a selection.
- `src/Praxis.Infrastructure/Ai/AiPlaneContract.cs` — three records grow fields; none grows a key.
- `src/Modules/Praxis.Modules.Tutor/Features/Sessions/SessionLifecycle.cs`, `TakeHint.cs`, `StreamHint.cs` + a Tutor migration.

**Python — modified**

- `ai/src/praxis_ai/items.py` — `ItemFormat`, passage/options on `Item`, the multiple-choice prompt and parser.
- `ai/src/praxis_ai/oracle.py` — `AnswerKey.correct_option`, the comparison branch.
- `ai/src/praxis_ai/key_store.py` — two columns, added idempotently.
- `ai/src/praxis_ai/routes/assessment.py`, `ai/src/praxis_ai/routes/oracle.py` — the wire.
- `contracts/ai-plane.openapi.json` — regenerated; the guard test is updated deliberately.

**Web — modified**

- `web/lib/api.ts` — types only.
- `web/lib/diagnostic.ts` — the pure answer-shape rules.
- `web/app/diagnostic/page.tsx` — passage pane + radio group.

**Pack — modified**

- `packs/english-reading/pack.yaml` — `format: multiple-choice` on fifteen Apply clauses; the comment block corrected to name only the forms that exist.

---

### Task 1: `ItemFormat`, declared in the pack and carried to the contract

**Files:**
- Create: `src/Praxis.Shared/ItemFormat.cs`
- Modify: `src/Modules/Praxis.Modules.Graph/Packs/PackDocument.cs:78-89`
- Modify: `src/Modules/Praxis.Modules.Graph/Domain/PoMContract.cs:144-175`
- Modify: `src/Modules/Praxis.Modules.Graph/Packs/PackValidator.cs`
- Modify: `src/Modules/Praxis.Modules.Graph/Packs/PackLoader.cs`
- Modify: `src/Modules/Praxis.Modules.Graph/Persistence/GraphDbContext.cs:99-111`
- Test: `tests/Praxis.Modules.Graph.Tests/PackValidatorTests.cs` (add to the existing file)

**Interfaces:**
- Produces: `Praxis.Shared.ItemFormat { Prose = 0, MultipleChoice = 1 }`; `PackEvidence.Format` (`ItemFormat`, default `Prose`); `AcceptedEvidence.Format` (`ItemFormat`); `AcceptedEvidence.Of(EvidenceType type, double minScore, double maxContribution, int requiredCount, ItemFormat format = ItemFormat.Prose)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Praxis.Modules.Graph.Tests/PackValidatorTests.cs`:

```csharp
[Fact]
public void A_format_may_only_be_declared_on_apply_evidence()
{
    var doc = OneConcept(evidence: new PackEvidence
    {
        Type = EvidenceType.Explain,
        MinScore = 0.7,
        Max = 0.20,
        Count = 1,
        Format = ItemFormat.MultipleChoice,
    });

    var result = PackValidator.Validate(doc);

    Assert.False(result.IsSuccess);
    Assert.Contains("apply", result.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Multiple_choice_without_a_declared_misconception_is_refused()
{
    // Distractors exist to catch a named wrong belief. With none declared there is nothing to
    // build them out of, and three of the four options would be noise.
    var doc = OneConcept(
        evidence: new PackEvidence
        {
            Type = EvidenceType.Apply,
            MinScore = 0.7,
            Max = 0.85,
            Count = 1,
            Format = ItemFormat.MultipleChoice,
        },
        misconceptions: []);

    var result = PackValidator.Validate(doc);

    Assert.False(result.IsSuccess);
    Assert.Contains("misconception", result.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Multiple_choice_apply_with_a_declared_misconception_is_accepted()
{
    var doc = OneConcept(
        evidence: new PackEvidence
        {
            Type = EvidenceType.Apply,
            MinScore = 0.7,
            Max = 0.85,
            Count = 1,
            Format = ItemFormat.MultipleChoice,
        },
        misconceptions: [new PackMisconception { Key = "keyword-match-wins", Statement = "I think the option that repeats the text is right." }]);

    Assert.True(PackValidator.Validate(doc).IsSuccess);
}
```

Add this helper to the same test class (adjust the field names only if the file already has an equivalent builder — reuse it if so):

```csharp
private static PackDocument OneConcept(
    PackEvidence evidence,
    List<PackMisconception>? misconceptions = null)
    => new()
    {
        Domain = "test-domain",
        Version = 1,
        Note = "a pack for one test",
        Concepts =
        [
            new PackConcept
            {
                Slug = "only-concept",
                Title = "Only concept",
                Summary = "The only concept in this pack.",
                Difficulty = 0.0,
                Minutes = 30,
                Misconceptions = misconceptions ??
                    [new PackMisconception { Key = "some-belief", Statement = "I think a wrong thing." }],
                Proof = new PackContract { Hold = 0.80, Evidence = [evidence] },
            },
        ],
    };
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Praxis.Modules.Graph.Tests --nologo --filter "FullyQualifiedName~PackValidatorTests"`
Expected: FAIL — `PackEvidence` has no `Format` member (compile error), and `ItemFormat` does not exist.

- [ ] **Step 3: Create the enum**

`src/Praxis.Shared/ItemFormat.cs`:

```csharp
namespace Praxis.Shared;

/// **The shape of an item, and therefore of the evidence it can produce.**
///
/// `Prose` is everything PRAXIS banked before M17: one question, answered in the learner's own
/// words, marked by a model that holds the answer on the far side of the AI-plane wire.
///
/// `MultipleChoice` is the other kind, and it is not a convenience. A fixed-key item is marked by
/// **comparing two integers** — no model, no judgement, no opinion — which makes it the purest
/// Apply evidence PRAXIS can collect, and the only kind whose correctness is a *fact*. The options
/// are on the learner's screen and are not secret. The index of the correct one is in a different
/// database, and this process cannot reach it.
public enum ItemFormat
{
    Prose = 0,
    MultipleChoice = 1,
}
```

- [ ] **Step 4: Carry it through the pack format and the domain**

In `src/Modules/Praxis.Modules.Graph/Packs/PackDocument.cs`, add to `PackEvidence`:

```csharp
    /// The shape items of this evidence type take. Legal only on `Apply` — a model-graded
    /// explanation has no options to choose between. Absent means `Prose`, which is every pack
    /// authored before M17.
    public ItemFormat Format { get; set; } = ItemFormat.Prose;
```

In `src/Modules/Praxis.Modules.Graph/Domain/PoMContract.cs`, add to `AcceptedEvidence` (beside `RequiredCount`) and thread it through `Of`:

```csharp
    /// The shape items proving this clause take. See <see cref="ItemFormat"/>.
    public ItemFormat Format { get; private set; }
```

Add `ItemFormat format = ItemFormat.Prose` as the **last, defaulted** parameter of `AcceptedEvidence.Of(...)` and assign `Format = format` in the object initialiser it returns. Defaulted last means every existing call site keeps compiling and keeps meaning `Prose`.

In `PackLoader.cs`, find where `AcceptedEvidence.Of(...)` is called from a `PackEvidence` and pass `e.Format` as the new argument.

- [ ] **Step 5: Add the validator rules**

In `PackValidator.cs`, inside the per-concept evidence loop, add:

```csharp
foreach (var clause in concept.Proof.Evidence)
{
    if (clause.Format is not ItemFormat.Prose && clause.Type is not EvidenceType.Apply)
    {
        return Result<Unit>.Invalid(
            $"'{concept.Slug}' declares format '{clause.Format}' on {clause.Type} evidence. A "
            + "format is a claim about how an item is *marked*, and only apply evidence is marked "
            + "against a fixed key. Explain and critique are graded by a model against a rubric; "
            + "there is nothing for them to choose between.");
    }

    if (clause.Format is ItemFormat.MultipleChoice && concept.Misconceptions.Count == 0)
    {
        return Result<Unit>.Invalid(
            $"'{concept.Slug}' declares multiple-choice apply evidence but no misconceptions. A "
            + "distractor exists to catch a *named* wrong belief — without one, three of the four "
            + "options are noise, a wrong answer diagnoses nothing, and the item is a coin toss "
            + "wearing a question mark.");
    }
}
```

Match the surrounding code's failure idiom exactly — if the file returns `Result<Unit>.Invalid(...)` use that; if it accumulates into a list of problems, append to that list instead.

- [ ] **Step 6: Map and migrate**

In `GraphDbContext.cs`, inside the `AcceptedEvidence` entity block, add:

```csharp
            e.Property(x => x.Format).IsRequired();
```

Then:

```bash
dotnet ef migrations add AcceptedEvidenceFormat --project src/Modules/Praxis.Modules.Graph --startup-project src/Praxis.Api
```

Open the generated migration and confirm it is exactly one `AddColumn<int>` on the graph schema's accepted-evidence table with `defaultValue: 0`. If it contains anything else, delete it, fix the model, and regenerate.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Praxis.Modules.Graph.Tests --nologo`
Expected: PASS, including every pre-existing pack test — nothing already authored declares a format, so everything stays `Prose`.

- [ ] **Step 8: Commit**

```bash
git add src/Praxis.Shared/ItemFormat.cs src/Modules/Praxis.Modules.Graph tests/Praxis.Modules.Graph.Tests
git commit -m "Item format 1/2 - a pack may declare how its apply evidence is marked (M17)"
```

---

### Task 2: The reading pack declares multiple choice, and stops describing forms that do not exist

**Files:**
- Modify: `packs/english-reading/pack.yaml` (the comment block at lines 21-33, and every `apply` clause)
- Test: `tests/Praxis.Modules.Graph.Tests/EnglishReadingPackTests.cs`

**Interfaces:**
- Consumes: `PackEvidence.Format` from Task 1.
- Produces: a shipped pack whose fifteen Apply clauses are `format: multiple-choice` — the fixture every later task generates against.

- [ ] **Step 1: Write the failing test**

Add to `tests/Praxis.Modules.Graph.Tests/EnglishReadingPackTests.cs`:

```csharp
[Fact]
public void Every_concept_proves_apply_by_a_fixed_key_multiple_choice()
{
    var doc = LoadShippedPack();   // reuse this file's existing loader helper

    Assert.Equal(15, doc.Concepts.Count);

    foreach (var concept in doc.Concepts)
    {
        var apply = concept.Proof!.Evidence.SingleOrDefault(e => e.Type == EvidenceType.Apply);

        Assert.True(apply is not null, $"'{concept.Slug}' has no apply clause to carry the floor.");
        Assert.Equal(ItemFormat.MultipleChoice, apply!.Format);

        // The validator refuses this combination, but say it here too: this is the property that
        // makes a distractor mean something.
        Assert.NotEmpty(concept.Misconceptions);
    }
}
```

If the existing file loads the pack through a differently-named helper, use that name — do not add a second loader.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Praxis.Modules.Graph.Tests --nologo --filter "FullyQualifiedName~EnglishReadingPackTests"`
Expected: FAIL — `Assert.Equal(MultipleChoice, Prose)` on the first concept.

- [ ] **Step 3: Declare the format on all fifteen Apply clauses**

In `packs/english-reading/pack.yaml`, every concept's proof block has an apply clause shaped like:

```yaml
    - type: apply
      min_score: 0.70
      max: 0.85
      count: 2
```

Add one line to each (match the existing indentation exactly):

```yaml
    - type: apply
      min_score: 0.70
      max: 0.85
      count: 2
      format: multiple-choice
```

Do not change `min_score`, `max` or `count` on any clause. Do not add `format` to `explain` or `critique` clauses — the validator from Task 1 rejects that.

- [ ] **Step 4: Correct the comment block**

Replace the `proof` paragraph at `packs/english-reading/pack.yaml:27-32` with:

```yaml
#   proof       CEILING: model-graded evidence (explain, critique) can never sum to hold.
#               FLOOR: apply alone is always sufficient. Apply, for reading, means: a NEVER-SEEN
#               passage answered against a FIXED KEY, marked by comparing the chosen option to the
#               key -- deterministic, unfakeable, and the key is never in the tutor's context (C-5).
#               Today that means four-option MULTIPLE CHOICE, and only that. True/false/not-given,
#               span selection and ordering are named, deliberate follow-ups (M19); they are not
#               described here as if they existed, because for a while this file described a marking
#               scheme the product did not have. No build/predict; a comprehension answer cannot run
#               against hidden tests. No recall; there is nothing here to memorize.
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Praxis.Modules.Graph.Tests --nologo`
Expected: PASS — the new test, plus the existing validator-gate and 15-count tests still green.

- [ ] **Step 6: Commit**

```bash
git add packs/english-reading/pack.yaml tests/Praxis.Modules.Graph.Tests/EnglishReadingPackTests.cs
git commit -m "Item format 2/2 - the reading pack declares the marking it actually gets (M17)"
```

---

### Task 3: The item grows a shape, and the schema guard grows teeth

**Files:**
- Modify: `src/Modules/Praxis.Modules.Assessment/Domain/BankedItem.cs`
- Modify: `src/Modules/Praxis.Modules.Assessment/Persistence/AssessmentDbContext.cs:48-73`
- Test: `tests/Praxis.Modules.Assessment.Tests/SchemaTests.cs`

**Interfaces:**
- Consumes: `Praxis.Shared.ItemFormat` from Task 1.
- Produces: `BankedItem.Format` (`ItemFormat`), `BankedItem.Passage` (`string?`), `BankedItem.Options` (`IReadOnlyList<string>?`), and `BankedItem.From(Guid id, Guid conceptId, EvidenceType evidenceType, string stem, string fingerprint, double difficulty, double discrimination = 1.0, double guessing = 0.0, Guid? familyId = null, string? targetsMisconception = null, string? oracleId = null, ItemFormat format = ItemFormat.Prose, string? passage = null, IReadOnlyList<string>? options = null)`.

- [ ] **Step 1: Write the failing tests**

In `tests/Praxis.Modules.Assessment.Tests/SchemaTests.cs`, extend the `Forbidden` array:

```csharp
    private static readonly string[] Forbidden =
    [
        "answer",
        "solution",
        "expected",
        "correct_response",
        "correct_option",
        "correct_index",
        "key_index",
        "key_text",
        "answer_key",
        "ground_truth",
        "rubric",
    ];
```

These are substring matches, so the existing `items.TimesCorrect` — a count of attempts, not a key — stays legal. Then add:

```csharp
/// **The options are not a secret and the index is.**
///
/// A learner is looking at all four choices; hiding them would be theatre. Which one is right is a
/// different kind of thing entirely, and it is not here — it went to `praxis_oracle`, a separate
/// database this process has no connection string for.
[Fact]
public async Task The_item_bank_holds_the_choices_and_never_which_one_is_right()
{
    var columns = (await ColumnsIn(AssessmentDbContext.Schema))
        .Where(c => c.Table == "items")
        .Select(c => c.Column)
        .ToList();

    Assert.Contains("Passage", columns);
    Assert.Contains("Options", columns);
    Assert.Contains("Format", columns);

    foreach (var forbidden in new[] { "CorrectOption", "CorrectIndex", "Key", "AnswerIndex" })
    {
        Assert.DoesNotContain(
            columns,
            c => c.Equals(forbidden, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Praxis.Modules.Assessment.Tests --nologo --filter "FullyQualifiedName~SchemaTests"`
Expected: FAIL — `Assert.Contains("Passage", columns)` finds nothing.

- [ ] **Step 3: Add the fields to the entity**

In `BankedItem.cs`, add after `Stem`:

```csharp
    /// The shape of this item, and therefore how it is marked. `Prose` is every item banked before
    /// M17 and its marking is unchanged.
    public ItemFormat Format { get; private set; } = ItemFormat.Prose;

    /// The text under study, for a reading item. **The learner reads it** — it is no more secret
    /// than the stem, and the tutor is given it so a hint can point at an actual sentence.
    public string? Passage { get; private set; }

    /// The choices, in the order they are shown. **Not a secret**: they are on the screen.
    ///
    /// Which one is correct is not here, is not nullable-and-empty here, and is not derivable from
    /// here. It is a single integer in the oracle's own database, and the only thing that has ever
    /// come back out of that database is one of three words.
    public IReadOnlyList<string>? Options { get; private set; }
```

Add the three parameters to `From` — **last and defaulted**, so every existing call site keeps compiling:

```csharp
        string? oracleId = null,
        ItemFormat format = ItemFormat.Prose,
        string? passage = null,
        IReadOnlyList<string>? options = null)
        => new()
        {
            // ... existing assignments unchanged ...
            OracleId = oracleId,
            Format = format,
            Passage = passage,
            Options = options,
        };
```

- [ ] **Step 4: Map the columns**

In `AssessmentDbContext.cs`, add these usings:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
```

and inside the `BankedItem` entity block:

```csharp
            item.Property(i => i.Format).IsRequired();
            item.Property(i => i.Passage).HasMaxLength(8_000);

            // The options ride as jsonb: they are read whole, every time, and never queried into.
            // A child table would buy ordering guarantees the list already has.
            var optionsToJson = new ValueConverter<IReadOnlyList<string>?, string?>(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null));

            var optionsComparer = new ValueComparer<IReadOnlyList<string>?>(
                (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
                v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal))),
                v => v == null ? null : v.ToList());

            item.Property(i => i.Options)
                .HasConversion(optionsToJson, optionsComparer)
                .HasColumnType("jsonb");
```

- [ ] **Step 5: Generate the migration**

```bash
dotnet ef migrations add ItemFormatPassageOptions --project src/Modules/Praxis.Modules.Assessment --startup-project src/Praxis.Api
```

Read the generated file. It must contain exactly three `AddColumn` calls on `items` — `Format` (`int`, not null, default 0), `Passage` (`text`, nullable, max 8000), `Options` (`jsonb`, nullable) — and nothing else. If it drops or recreates an index, discard it and regenerate.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Praxis.Modules.Assessment.Tests --nologo`
Expected: PASS. `No_column_in_the_assessment_schema_could_be_an_answer` must still pass with the widened `Forbidden` list — if it fails, a column you just added is named like a key, and the column is wrong, not the test.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Praxis.Modules.Assessment tests/Praxis.Modules.Assessment.Tests/SchemaTests.cs
git commit -m "The item grows a passage and four choices, and the guard grows three new banned words (M17)"
```

---

### Task 4: The generator writes a passage and four options, and the index never comes back

**Files:**
- Modify: `ai/src/praxis_ai/items.py`
- Modify: `ai/src/praxis_ai/oracle.py` (the `AnswerKey` dataclass only)
- Test: `ai/tests/test_items.py`

**Interfaces:**
- Produces: `ItemFormat` (`StrEnum`: `PROSE = "prose"`, `MULTIPLE_CHOICE = "multiple_choice"`); `Item.passage: str | None`, `Item.options: tuple[str, ...] | None`; `AnswerKey.correct_option: int | None = None`, `AnswerKey.option_misconceptions: dict[int, str]` (default empty); `ItemGenerator.generate(..., item_format: ItemFormat = ItemFormat.PROSE)`.
- Consumed by: Task 5 (the oracle reads the key), Task 6 (the routes).

- [ ] **Step 1: Write the failing tests**

Add to `ai/tests/test_items.py` (reuse the module's existing fake Claude client — match its name; if it is `FakeClaude(text=...)`, use that):

```python
import pytest

from praxis_ai.items import Item, ItemFormat, ItemGenerator, ItemRejectedError, fingerprint_of
from praxis_ai.oracle import InMemoryAnswerKeyStore

_MCQ_REPLY = """{
  "passage": "The new roster system goes live on Monday. Everyone who worked a Saturday shift last month has already been moved across. However, if you swapped a Saturday with a colleague and did not tell the office, the system has no record of it.",
  "question": "Whose roster is the system most likely to get wrong?",
  "options": [
    "Everyone who worked a Saturday shift last month.",
    "Anyone who swapped a shift without telling the office.",
    "All warehouse staff.",
    "Anyone who calls the front desk."
  ],
  "correct": 1,
  "option_misconceptions": {"0": "keyword-match-wins", "2": "literal-only"},
  "misconceptions": {"keyword-match-wins": "I think the option repeating the text is right."},
  "targets": "keyword-match-wins"
}"""


@pytest.mark.asyncio
async def test_a_multiple_choice_item_carries_a_passage_and_options():
    store = InMemoryAnswerKeyStore()
    generator = ItemGenerator(FakeClaude(_MCQ_REPLY), store)

    item = await generator.generate(
        concept_id="c1",
        concept_summary="Reading connectives that reverse a claim.",
        evidence_type=EvidenceType.APPLY,
        difficulty=0.5,
        attribution=Attribution(learner_id="l1", concept_id="c1"),
        known_misconceptions={"keyword-match-wins": "...", "literal-only": "..."},
        item_format=ItemFormat.MULTIPLE_CHOICE,
    )

    assert item.passage is not None and "However" in item.passage
    assert item.options == (
        "Everyone who worked a Saturday shift last month.",
        "Anyone who swapped a shift without telling the office.",
        "All warehouse staff.",
        "Anyone who calls the front desk.",
    )


@pytest.mark.asyncio
async def test_the_correct_index_goes_to_the_store_and_not_to_the_caller():
    store = InMemoryAnswerKeyStore()
    generator = ItemGenerator(FakeClaude(_MCQ_REPLY), store)

    item = await generator.generate(
        concept_id="c1",
        concept_summary="s",
        evidence_type=EvidenceType.APPLY,
        difficulty=0.5,
        attribution=Attribution(learner_id="l1", concept_id="c1"),
        known_misconceptions={"keyword-match-wins": "...", "literal-only": "..."},
        item_format=ItemFormat.MULTIPLE_CHOICE,
    )

    # The type has nowhere to put it, so this is belt and braces on the type's promise.
    assert not any("correct" in f for f in vars(item))

    key = store.get(item.id)
    assert key is not None
    assert key.correct_option == 1
    assert key.option_misconceptions == {0: "keyword-match-wins", 2: "literal-only"}


@pytest.mark.asyncio
async def test_a_distractor_belief_outside_the_declared_set_is_discarded():
    reply = _MCQ_REPLY.replace('"2": "literal-only"', '"2": "invented-on-the-spot"')
    store = InMemoryAnswerKeyStore()
    generator = ItemGenerator(FakeClaude(reply), store)

    item = await generator.generate(
        concept_id="c1",
        concept_summary="s",
        evidence_type=EvidenceType.APPLY,
        difficulty=0.5,
        attribution=Attribution(learner_id="l1", concept_id="c1"),
        known_misconceptions={"keyword-match-wins": "..."},
        item_format=ItemFormat.MULTIPLE_CHOICE,
    )

    assert store.get(item.id).option_misconceptions == {0: "keyword-match-wins"}


@pytest.mark.asyncio
async def test_a_multiple_choice_item_without_four_options_is_rejected():
    reply = _MCQ_REPLY.replace('    "Anyone who calls the front desk."\n', "")
    generator = ItemGenerator(FakeClaude(reply), InMemoryAnswerKeyStore())

    with pytest.raises(ItemRejectedError):
        await generator.generate(
            concept_id="c1",
            concept_summary="s",
            evidence_type=EvidenceType.APPLY,
            difficulty=0.5,
            attribution=Attribution(learner_id="l1", concept_id="c1"),
            item_format=ItemFormat.MULTIPLE_CHOICE,
        )


def test_a_prose_fingerprint_is_unchanged_by_the_passage_field():
    # Every item already banked must keep the fingerprint it was banked with, or `already_seen`
    # stops meaning anything and the bank fills with duplicates.
    assert fingerprint_of("What does 'however' do?") == fingerprint_of(
        "What does 'however' do?", passage=None
    )


def test_two_questions_about_the_same_passage_do_not_collide_and_two_identical_ones_do():
    passage = "A short passage about a roster."
    assert fingerprint_of("Who acts?", passage=passage) != fingerprint_of("When?", passage=passage)
    assert fingerprint_of("Who acts?", passage=passage) == fingerprint_of("Who acts?", passage=passage)
    assert fingerprint_of("Who acts?", passage=passage) != fingerprint_of("Who acts?")
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd ai && uv run pytest tests/test_items.py -q`
Expected: FAIL — `ImportError: cannot import name 'ItemFormat'`.

- [ ] **Step 3: Add the format, the fields, and the key**

In `ai/src/praxis_ai/items.py`, add beside `EvidenceType`:

```python
class ItemFormat(StrEnum):
    """How an item is answered, and therefore how it is marked.

    ``MULTIPLE_CHOICE`` is marked by comparing two integers — no model, no judgement, no cost. It
    is the only marking in PRAXIS whose verdict is a *fact* rather than an opinion.
    """

    PROSE = "prose"
    MULTIPLE_CHOICE = "multiple_choice"
```

Add to the `Item` dataclass, after `stem`:

```python
    #: The text under study, for a reading item. The learner reads it; it is not a secret.
    passage: str | None = None

    #: The choices as shown, in order. **Not a secret** — they are on the screen. Which one is
    #: correct is not here, and there is no field it could be put in.
    options: tuple[str, ...] | None = None
```

Replace `fingerprint_of`:

```python
def fingerprint_of(stem: str, passage: str | None = None) -> str:
    """A stem's identity, robust to the cosmetic edits a model makes when told to 'vary' something.

    When there is a passage it is part of the identity — otherwise a generator could hand back the
    same passage with a reworded question forever and every one would look new. With no passage the
    input is the stem alone, byte for byte as before, so **every fingerprint already banked stays
    valid**.
    """
    subject = stem if passage is None else f"{passage}\n{stem}"
    normalised = re.sub(r"[^a-z0-9\s]", "", subject.lower())
    normalised = re.sub(r"\s+", " ", normalised).strip()

    return hashlib.sha256(normalised.encode()).hexdigest()[:32]
```

In `ai/src/praxis_ai/oracle.py`, add to the `AnswerKey` dataclass:

```python
    #: The index of the correct option, or ``None`` for a prose item. **The only integer in PRAXIS
    #: that is genuinely dangerous**, and it lives in this module and the store behind it.
    correct_option: int | None = None

    #: Option index → the **declared** wrong belief that choosing it reveals. Authored, not
    #: inferred: a distractor is a diagnosis somebody wrote down in advance.
    option_misconceptions: dict[int, str] = field(default_factory=dict)
```

Add `field` to the existing `from dataclasses import dataclass` import in `oracle.py`.

- [ ] **Step 4: Teach the generator the multiple-choice shape**

Add `item_format: ItemFormat = ItemFormat.PROSE` as a keyword parameter of `ItemGenerator.generate` and pass it into `_draft`. In `_draft`, choose the prompt and pass `item_format` to `_parse`, then build:

```python
        drafted = _parse(completion.text, known_keys, item_format)

        passage = drafted.get("passage")
        options = drafted.get("options")

        item_id = f"item-{fingerprint_of(drafted['question'], passage)[:16]}"

        item = Item(
            id=item_id,
            concept_id=concept_id,
            evidence_type=evidence_type,
            stem=drafted["question"],
            difficulty=difficulty,
            fingerprint=fingerprint_of(drafted["question"], passage),
            targets_misconception=drafted["targets"],
            passage=passage,
            options=None if options is None else tuple(options),
        )
```

and on the `AnswerKey`, add:

```python
            correct_option=drafted.get("correct"),
            option_misconceptions=drafted.get("option_misconceptions", {}),
```

For a multiple-choice draft, `answer` is the correct option's text — keep filing it, so a key row is never empty and the store's `answer NOT NULL` holds.

Add the system prompt beside `_SYSTEM`:

```python
_SYSTEM_MULTIPLE_CHOICE = """You write reading-comprehension items. You produce a short passage, \
one question about it, exactly four options of which exactly one is correct, the index of that \
option, and — for each wrong option — which of the reader's known wrong habits it is built to catch.

The passage and the options will be shown to a learner. Which option is correct will NOT be: it \
goes to a marking oracle the tutor cannot read.

Rules:
- The passage is self-contained, 80-150 words, everyday or workplace English. Everything needed to \
answer is in it. Never refer to anything outside it.
- The question must be answerable ONLY by understanding the passage. A reader who has not read it \
must not be able to guess from general knowledge.
- Exactly four options. Exactly one correct. All four plausible to someone who misread; an option \
nobody would pick is a wasted option.
- Each wrong option should be built to catch a specific KNOWN WRONG HABIT from the list you are \
given, named by its key in `option_misconceptions`. Only keys from that list are valid.
- `correct` is the 0-based index of the correct option.

Reply with ONLY a JSON object:
{"passage": "...", "question": "...", "options": ["...", "...", "...", "..."], "correct": 0, \
"option_misconceptions": {"1": "<declared key>"}, "answer": "the correct option's text", \
"misconceptions": {"slug": "the wrong belief, stated"}, "targets": "<declared key>" | null}

Do not explain. Do not add commentary. The JSON object is your entire reply."""
```

- [ ] **Step 5: Parse and validate it**

Give `_parse` the signature `_parse(text: str, known_keys: frozenset[str], item_format: ItemFormat = ItemFormat.PROSE) -> dict`. Keep every existing prose check untouched, and after them add:

```python
    if item_format is not ItemFormat.MULTIPLE_CHOICE:
        return {...}   # the existing return, unchanged

    passage = parsed.get("passage")
    options = parsed.get("options")
    correct = parsed.get("correct")

    if not isinstance(passage, str) or not passage.strip():
        raise ItemRejectedError("A reading item with no passage is not a reading item.")

    if (
        not isinstance(options, list)
        or len(options) != 4
        or not all(isinstance(o, str) and o.strip() for o in options)
    ):
        raise ItemRejectedError(
            "A fixed-key item needs exactly four non-empty options. Fewer is a different item with "
            "a different guessing floor, and the estimator was told to expect four."
        )

    if not isinstance(correct, int) or isinstance(correct, bool) or not 0 <= correct < 4:
        raise ItemRejectedError(
            "The correct option must be an index into the four options. Without it the item is "
            "unmarkable, and a learner would spend real effort on it for nothing."
        )

    # The airlock, again: a distractor may only name a belief the caller declared in advance.
    catches: dict[int, str] = {}

    for raw_index, key in (parsed.get("option_misconceptions") or {}).items():
        try:
            index = int(raw_index)
        except (TypeError, ValueError):
            continue

        if index == correct or not 0 <= index < 4:
            continue

        if key not in known_keys:
            log.warning(
                "Generator proposed a distractor belief outside the declared set. Discarded.",
                extra={"key": key},
            )
            continue

        catches[index] = key

    return {
        "question": question.strip(),
        "answer": answer.strip(),
        "misconceptions": {k.strip(): v.strip() for k, v in misconceptions.items()},
        "targets": targets,
        "passage": passage.strip(),
        "options": [o.strip() for o in options],
        "correct": correct,
        "option_misconceptions": catches,
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd ai && uv run pytest tests/test_items.py -q && uv run ruff check .`
Expected: PASS, and every pre-existing prose generation test still green.

- [ ] **Step 7: Commit**

```bash
git add ai/src/praxis_ai/items.py ai/src/praxis_ai/oracle.py ai/tests/test_items.py
git commit -m "The generator writes a passage and four options, and the index goes only one way (M17)"
```

---

### Task 5: The oracle marks by comparison, and calls no model to do it

**Files:**
- Modify: `ai/src/praxis_ai/oracle.py`
- Modify: `ai/src/praxis_ai/key_store.py`
- Test: `ai/tests/test_oracle.py`, `ai/tests/test_key_store.py`

**Interfaces:**
- Consumes: `AnswerKey.correct_option`, `AnswerKey.option_misconceptions` from Task 4.
- Produces: `Oracle.check(item_id: str, claim: str, attribution: Attribution, selected_option: int | None = None) -> Verdict`; `FormatMismatchError(RuntimeError)`.

- [ ] **Step 1: Write the failing tests**

Add to `ai/tests/test_oracle.py`:

```python
class ExplodingClaude:
    """A client that fails the test if it is touched. Deterministic marking means *no model*."""

    async def complete(self, *args, **kwargs):
        raise AssertionError(
            "The oracle called a model to mark a fixed-key item. That is the whole thing this "
            "milestone exists to stop: a comparison became an opinion."
        )


_KEY = AnswerKey(
    item_id="item-1",
    concept_id="c1",
    question="Whose roster is wrong?",
    answer="Anyone who swapped without telling the office.",
    misconceptions={"keyword-match-wins": "I think the repeating option is right."},
    correct_option=1,
    option_misconceptions={0: "keyword-match-wins"},
)


@pytest.mark.asyncio
async def test_the_right_index_is_correct_and_costs_nothing():
    oracle = Oracle(ExplodingClaude(), InMemoryAnswerKeyStore({"item-1": _KEY}))

    verdict = await oracle.check("item-1", "", Attribution("l1", "c1"), selected_option=1)

    assert verdict.outcome is ClaimOutcome.CORRECT
    assert verdict.misconception is None
    assert verdict.cost.usd == 0


@pytest.mark.asyncio
async def test_a_distractor_names_the_belief_its_author_built_it_to_catch():
    oracle = Oracle(ExplodingClaude(), InMemoryAnswerKeyStore({"item-1": _KEY}))

    verdict = await oracle.check("item-1", "", Attribution("l1", "c1"), selected_option=0)

    assert verdict.outcome is ClaimOutcome.WRONG
    assert verdict.misconception == "keyword-match-wins"


@pytest.mark.asyncio
async def test_a_distractor_with_no_declared_belief_is_wrong_and_says_nothing_more():
    oracle = Oracle(ExplodingClaude(), InMemoryAnswerKeyStore({"item-1": _KEY}))

    verdict = await oracle.check("item-1", "", Attribution("l1", "c1"), selected_option=2)

    assert verdict.outcome is ClaimOutcome.WRONG
    assert verdict.misconception is None


@pytest.mark.asyncio
async def test_prose_sent_to_a_fixed_key_item_is_refused_rather_than_guessed():
    oracle = Oracle(ExplodingClaude(), InMemoryAnswerKeyStore({"item-1": _KEY}))

    with pytest.raises(FormatMismatchError):
        await oracle.check("item-1", "I think it is the swappers", Attribution("l1", "c1"))


@pytest.mark.asyncio
async def test_a_selection_on_a_prose_item_is_refused_rather_than_guessed():
    prose = AnswerKey(
        item_id="item-2", concept_id="c1", question="q", answer="a", misconceptions={}
    )
    oracle = Oracle(ExplodingClaude(), InMemoryAnswerKeyStore({"item-2": prose}))

    with pytest.raises(FormatMismatchError):
        await oracle.check("item-2", "", Attribution("l1", "c1"), selected_option=0)


@pytest.mark.asyncio
async def test_an_index_outside_the_options_is_refused():
    oracle = Oracle(ExplodingClaude(), InMemoryAnswerKeyStore({"item-1": _KEY}))

    with pytest.raises(FormatMismatchError):
        await oracle.check("item-1", "", Attribution("l1", "c1"), selected_option=9)
```

Add to `ai/tests/test_key_store.py` (this file's tests already skip when no Postgres is configured — follow the same guard):

```python
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
        AnswerKey(item_id="item-prose", concept_id="c1", question="q", answer="a", misconceptions={})
    )

    got = store.get("item-prose")

    assert got.correct_option is None
    assert got.option_misconceptions == {}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd ai && uv run pytest tests/test_oracle.py tests/test_key_store.py -q`
Expected: FAIL — `ImportError: cannot import name 'FormatMismatchError'`.

- [ ] **Step 3: Add the error and the comparison branch**

In `ai/src/praxis_ai/oracle.py`, beside `OracleUnclearError`:

```python
class FormatMismatchError(RuntimeError):
    """The answer arrived in the wrong shape for the item.

    Deliberately **not** a fallback to the model. A fixed-key item answered in prose is a bug in
    the caller, and quietly marking it with a language model would turn a deterministic verdict
    into an opinion while every test stayed green — which is precisely the failure the fixed key
    exists to remove.
    """
```

Replace the head of `Oracle.check`:

```python
    async def check(
        self,
        item_id: str,
        claim: str,
        attribution: Attribution,
        selected_option: int | None = None,
    ) -> Verdict:
        key = self._store.get(item_id)

        if key is None:
            raise UnknownItemError(
                f"No answer key for item {item_id!r}. PRAXIS will not improvise a verdict about a "
                "learner from an item it cannot mark."
            )

        if key.correct_option is not None:
            return self._mark_fixed_key(key, selected_option, attribution)

        if selected_option is not None:
            raise FormatMismatchError(
                f"Item {item_id!r} is marked from prose, but a selected option arrived. PRAXIS "
                "does not guess which one the learner meant."
            )

        # ... the existing model-marking path continues here, unchanged ...
```

And add the marker, which touches no client:

```python
    def _mark_fixed_key(
        self, key: AnswerKey, selected_option: int | None, attribution: Attribution
    ) -> Verdict:
        """**Two integers.** No model, no prompt, no cost, and no room for an opinion.

        There is no `PARTIAL` here and there cannot be: you either chose the option or you did not.
        A grader that could call a four-way choice "partially right" is a grader that can be argued
        with, and this one cannot be.
        """
        if selected_option is None:
            raise FormatMismatchError(
                f"Item {key.item_id!r} is marked against a fixed key, but no option was selected."
            )

        if not 0 <= selected_option < 4:
            raise FormatMismatchError(
                f"{selected_option} is not one of this item's options."
            )

        correct = selected_option == key.correct_option

        misconception = None if correct else key.option_misconceptions.get(selected_option)

        # The same airlock the model path uses: only a belief the item's author declared.
        if misconception is not None and misconception not in key.misconceptions:
            log.warning(
                "A distractor named a belief outside the item's declared set. Discarded.",
                extra={"item_id": key.item_id},
            )
            misconception = None

        log.info(
            "claim checked",
            extra={
                "item_id": key.item_id,
                "concept_id": key.concept_id,
                "learner_id": attribution.learner_id,
                "outcome": "correct" if correct else "wrong",
                "misconception": misconception,
            },
        )

        return Verdict(
            outcome=ClaimOutcome.CORRECT if correct else ClaimOutcome.WRONG,
            misconception=misconception,
            cost=Cost(usd=0),
        )
```

If `Cost` requires more than a `usd` argument, construct it the way the module's other zero-cost paths do — check `ai/src/praxis_ai/cost.py` and match.

- [ ] **Step 4: Widen the durable store**

In `ai/src/praxis_ai/key_store.py`, extend `_SCHEMA` and add the idempotent upgrade for databases that already exist:

```python
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

_UPGRADE = """
ALTER TABLE answer_keys ADD COLUMN IF NOT EXISTS correct_option integer;
ALTER TABLE answer_keys ADD COLUMN IF NOT EXISTS option_misconceptions jsonb NOT NULL DEFAULT '{}'::jsonb;
"""
```

`ensure_schema` runs both. `add` inserts the two new values; `get` selects and rebuilds them, converting the jsonb map's string keys back to ints:

```python
        return AnswerKey(
            item_id=row[0],
            concept_id=row[1],
            question=row[2],
            answer=row[3],
            misconceptions=dict(row[4]),
            correct_option=row[5],
            option_misconceptions={int(k): v for k, v in dict(row[6] or {}).items()},
        )
```

In `add`, serialise with `Jsonb({str(k): v for k, v in key.option_misconceptions.items()})` — jsonb object keys are strings.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd ai && uv run pytest -q && uv run ruff check .`
Expected: PASS across the whole AI suite — the prose oracle tests must be untouched.

- [ ] **Step 6: Commit**

```bash
git add ai/src/praxis_ai/oracle.py ai/src/praxis_ai/key_store.py ai/tests
git commit -m "The oracle marks a fixed-key item by comparing two integers, and calls no model (M17)"
```

---

### Task 6: The wire carries the format in and the selection back

**Files:**
- Modify: `ai/src/praxis_ai/routes/assessment.py:124-195`
- Modify: `ai/src/praxis_ai/routes/oracle.py:54-106`
- Modify: `contracts/ai-plane.openapi.json` (regenerated)
- Test: `ai/tests/test_routes_assessment.py`, `ai/tests/test_routes_oracle.py`, and the contract-guard test that pins the OpenAPI surface

**Interfaces:**
- Consumes: `ItemFormat`, `Item.passage/options`, `Oracle.check(..., selected_option=)` from Tasks 4–5.
- Produces: `POST /assessment/items` accepting `item_format`, returning `passage` and `options`; `POST /oracle/check-claim` accepting `selected_option` with `claim` optional.

- [ ] **Step 1: Write the failing tests**

Add to `ai/tests/test_routes_assessment.py`:

```python
def test_a_multiple_choice_request_returns_a_passage_and_options_and_no_key(client, fake_claude):
    fake_claude.reply(_MCQ_REPLY)   # reuse this module's client fixture idiom

    response = client.post(
        "/assessment/items",
        json={
            "concept_id": "c1",
            "concept_summary": "Connectives that reverse a claim.",
            "evidence_type": "apply",
            "difficulty": 0.5,
            "learner_id": "l1",
            "item_format": "multiple_choice",
            "known_misconceptions": {"keyword-match-wins": "..."},
        },
    )

    assert response.status_code == 200
    body = response.json()

    assert body["passage"]
    assert len(body["options"]) == 4

    # Structural, not a substring scan: no *property* on the way out may be a key.
    assert not any(
        k in body for k in ("correct", "correct_option", "answer", "key", "option_misconceptions")
    )
```

Add to `ai/tests/test_routes_oracle.py`:

```python
def test_a_selection_marks_without_a_claim(client, seeded_store):
    seeded_store.add(
        AnswerKey(
            item_id="item-1",
            concept_id="c1",
            question="q",
            answer="the second",
            misconceptions={"keyword-match-wins": "..."},
            correct_option=1,
            option_misconceptions={0: "keyword-match-wins"},
        )
    )

    response = client.post(
        "/oracle/check-claim",
        json={
            "item_id": "item-1",
            "selected_option": 0,
            "learner_id": "l1",
            "concept_id": "c1",
        },
    )

    assert response.status_code == 200
    assert response.json() == {
        "outcome": "wrong",
        "misconception": "keyword-match-wins",
        "usd": 0.0,
    }


def test_neither_a_claim_nor_a_selection_is_a_422(client):
    response = client.post(
        "/oracle/check-claim",
        json={"item_id": "item-1", "learner_id": "l1", "concept_id": "c1"},
    )

    assert response.status_code == 422
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd ai && uv run pytest tests/test_routes_assessment.py tests/test_routes_oracle.py -q`
Expected: FAIL — 422 on the unknown `item_format` field.

- [ ] **Step 3: Widen the item route**

In `ai/src/praxis_ai/routes/assessment.py`, import `ItemFormat` from `praxis_ai.items`, add to `GenerateItemRequest`:

```python
    #: How items for this concept are marked. Declared by the pack, carried by the core.
    item_format: ItemFormat = ItemFormat.PROSE
```

add to `ItemResponse`:

```python
    #: The text under study, for a reading item. The learner reads it; it is not secret.
    passage: str | None = None

    #: The choices as shown, in order. Which one is correct is not here and never will be.
    options: list[str] | None = None
```

pass `item_format=request.item_format` into `generator.generate(...)`, and return `passage=item.passage, options=None if item.options is None else list(item.options)`.

- [ ] **Step 4: Widen the oracle route**

In `ai/src/praxis_ai/routes/oracle.py`, import `FormatMismatchError`, and replace `CheckClaimRequest`:

```python
class CheckClaimRequest(BaseModel):
    item_id: str = Field(min_length=1)

    #: What the learner actually said, for a prose item. It goes to the oracle and nowhere else.
    claim: str = Field(default="", max_length=8_000)

    #: The option they chose, for a fixed-key item. Exactly one of these two arrives.
    selected_option: int | None = Field(default=None, ge=0)

    learner_id: str = Field(min_length=1)
    concept_id: str = Field(min_length=1)

    @model_validator(mode="after")
    def exactly_one_answer(self) -> "CheckClaimRequest":
        if bool(self.claim.strip()) == (self.selected_option is not None):
            raise ValueError(
                "An answer is either words or a choice, and PRAXIS is told which. Both, or "
                "neither, is a caller that does not know what it is submitting."
            )
        return self
```

Import `model_validator` from `pydantic`. Pass `selected_option=request.selected_option` into `oracle.check(...)`, and add:

```python
    except FormatMismatchError as mismatch:
        # 422, never a fallback. See FormatMismatchError.
        raise HTTPException(status_code=422, detail=str(mismatch)) from mismatch
```

- [ ] **Step 5: Regenerate the contract**

Never hand-edit the JSON:

```bash
cd ai && uv run python scripts/export_contract.py
```

Then update **both** guards deliberately — `ai/tests/test_contract.py` and `tests/Praxis.Infrastructure.Tests/Ai/AiPlaneContractTests.cs`. Add the new fields to their expectations, and add an assertion to the Python guard that no property named `correct`, `correct_option`, `answer`, `key` or `option_misconceptions` appears anywhere in the emitted schema:

```python
def test_the_published_contract_has_no_property_that_could_be_a_key():
    schema = json.loads(CONTRACT.read_text(encoding="utf-8"))

    forbidden = {"correct", "correct_option", "correct_index", "answer", "key", "option_misconceptions"}
    found = [name for name in _all_property_names(schema) if name.lower() in forbidden]

    assert not found, (
        f"The published AI-plane contract advertises {found}. The contract is the thing other "
        "people build against — a key in it is a key in every client that will ever be written."
    )
```

Write `_all_property_names` as a recursive walk over the schema's `properties` maps, collecting keys.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd ai && uv run pytest -q && uv run ruff check .`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add ai/src/praxis_ai/routes contracts/ai-plane.openapi.json ai/tests
git commit -m "The wire carries a format in and a selection back, and still has no field for a key (M17)"
```

---

### Task 7: The core asks for the right shape and banks it with an honest guessing floor

**Files:**
- Modify: `src/Praxis.Infrastructure/Ai/AiPlaneContract.cs:183-215`
- Modify: `src/Modules/Praxis.Modules.Assessment/IConceptIntel.cs`
- Modify: `src/Praxis.Api/Composition/GraphConceptIntel.cs`
- Modify: `src/Modules/Praxis.Modules.Assessment/Features/Diagnostics/EnsureConceptPool.cs:86-119`
- Test: `tests/Praxis.Modules.Assessment.Tests/EnsureConceptPoolTests.cs`

**Interfaces:**
- Consumes: `ItemFormat` (Task 1), `BankedItem.From(..., format, passage, options)` (Task 3), the widened AI-plane routes (Task 6).
- Produces: `GenerateItemRequest(..., ItemFormat ItemFormat)`; `ItemResponse(..., string? Passage, IReadOnlyList<string>? Options)`; `ConceptIntel(string Summary, double DifficultyTarget, IReadOnlyDictionary<string, string> DeclaredMisconceptions, ItemFormat ApplyFormat)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Praxis.Modules.Assessment.Tests/EnsureConceptPoolTests.cs` (use this file's existing fake `IAiPlane` and `IConceptIntel` — extend them rather than adding new ones):

```csharp
[Fact]
public async Task A_multiple_choice_concept_banks_the_passage_the_options_and_a_real_guessing_floor()
{
    var intel = new FakeIntel(applyFormat: ItemFormat.MultipleChoice);
    var ai = new FakeAiPlane(item => item with
    {
        Passage = "A short passage about a roster.",
        Options = ["A", "B", "C", "D"],
    });

    var handler = HandlerWith(ai, intel);

    var result = await handler.HandleAsync(new EnsureConceptPool.Command(ConceptId), default);

    Assert.True(result.IsSuccess);

    var banked = await Db.Items.AsNoTracking().FirstAsync(i => i.ConceptId == ConceptId);

    Assert.Equal(ItemFormat.MultipleChoice, banked.Format);
    Assert.Equal("A short passage about a roster.", banked.Passage);
    Assert.Equal(4, banked.Options!.Count);

    // A four-way guess is right a quarter of the time. An item banked at c = 0 tells the
    // estimator that a coin flip is knowledge, and inflates every learner who takes it.
    Assert.Equal(0.25, banked.Guessing, precision: 6);
}

[Fact]
public async Task The_requested_format_is_the_one_the_concept_declares()
{
    var intel = new FakeIntel(applyFormat: ItemFormat.MultipleChoice);
    var ai = new FakeAiPlane();

    await HandlerWith(ai, intel).HandleAsync(new EnsureConceptPool.Command(ConceptId), default);

    Assert.All(ai.Requests, r => Assert.Equal(ItemFormat.MultipleChoice, r.ItemFormat));
}

[Fact]
public async Task A_prose_concept_still_banks_a_prose_item_with_no_guessing_floor()
{
    var handler = HandlerWith(new FakeAiPlane(), new FakeIntel(applyFormat: ItemFormat.Prose));

    await handler.HandleAsync(new EnsureConceptPool.Command(ConceptId), default);

    var banked = await Db.Items.AsNoTracking().FirstAsync(i => i.ConceptId == ConceptId);

    Assert.Equal(ItemFormat.Prose, banked.Format);
    Assert.Null(banked.Passage);
    Assert.Null(banked.Options);
    Assert.Equal(0.0, banked.Guessing, precision: 6);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Praxis.Modules.Assessment.Tests --nologo --filter "FullyQualifiedName~EnsureConceptPool"`
Expected: FAIL — `ConceptIntel` has no `ApplyFormat`, `ItemResponse` has no `Passage`.

- [ ] **Step 3: Widen the contract records**

In `AiPlaneContract.cs`, add to `GenerateItemRequest` as the last parameter:

```csharp
    /// The shape the item must take, declared by the pack and carried here. The AI plane defaults
    /// to prose when it is absent, which is every caller written before M17.
    ItemFormat ItemFormat = ItemFormat.Prose);
```

and to `ItemResponse`:

```csharp
    /// The text under study, for a reading item. **Not a secret** — the learner reads it, and the
    /// tutor is given it so a hint can point at a real sentence.
    string? Passage = null,

    /// The choices as shown, in order. **Not a secret** — they are on the learner's screen.
    ///
    /// Which one is correct is not on this record. It is not nullable-and-empty on this record. It
    /// is an integer in a database this process has no connection string for.
    IReadOnlyList<string>? Options = null);
```

and to `CheckClaimRequest` (Task 8 sends it; it belongs with the other contract changes):

```csharp
    /// The option the learner chose, for a fixed-key item — and **nothing else about it**. The
    /// index of the *correct* one is not here, is not on the response, and is not obtainable from
    /// this process: it is in `praxis_oracle`, which this connection string does not reach.
    int? SelectedOption = null);
```

All three are defaulted and last, so no existing construction breaks.

- [ ] **Step 4: Carry the concept's format across the seam**

Add to `ConceptIntel` as the last parameter:

```csharp
    /// The shape this concept's apply items take, from its proof contract.
    ItemFormat ApplyFormat = ItemFormat.Prose);
```

In `GraphConceptIntel.ForAsync`, read the contract's Apply clause and pass it:

```csharp
        var applyFormat = await _db.Contracts.AsNoTracking()
            .Where(c => c.ConceptId == conceptId)
            .SelectMany(c => c.Accepted)
            .Where(a => a.Type == EvidenceType.Apply)
            .Select(a => (ItemFormat?)a.Format)
            .FirstOrDefaultAsync(ct) ?? ItemFormat.Prose;

        return new ConceptIntel(
            concept.Summary,
            Math.Clamp((concept.DifficultyPrior + 3.0) / 6.0, 0.0, 1.0),
            declared,
            applyFormat);
```

If `PoMContract.Accepted` is not a navigation EF can traverse, query `AcceptedEvidence` directly through its own `DbSet` joined on `PoMContractId` — check `GraphDbContext` and match what is actually mapped.

- [ ] **Step 5: Bank the shape**

In `EnsureConceptPool.Handler.HandleAsync`, add `ItemFormat: intel.ApplyFormat` to the `GenerateItemRequest`, and replace the `BankedItem.From(...)` call:

```csharp
                generated.Add(BankedItem.From(
                    Guid.CreateVersion7(),
                    command.ConceptId,
                    item.EvidenceType,
                    item.Stem,
                    item.Fingerprint,
                    difficulty: (Math.Clamp(item.Difficulty, 0.0, 1.0) * 6.0) - 3.0,
                    // **A coin has a θ too.** An N-way choice is right 1/N of the time whatever the
                    // learner knows, and the 3PL estimator must be told so — an item banked at zero
                    // reads a lucky guess as evidence and inflates everyone who takes it.
                    guessing: item.Options is { Count: > 0 } shown ? 1.0 / shown.Count : 0.0,
                    targetsMisconception: item.TargetsMisconception,
                    oracleId: item.Id,
                    format: intel.ApplyFormat,
                    passage: item.Passage,
                    options: item.Options));
```

`BankedItem.From` takes `discrimination` and `familyId` positionally before these; pass every argument from `difficulty` onward **by name** as shown, so nothing lands in the wrong slot.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Praxis.Modules.Assessment.Tests --nologo`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Praxis.Infrastructure/Ai/AiPlaneContract.cs src/Modules/Praxis.Modules.Assessment src/Praxis.Api/Composition/GraphConceptIntel.cs tests/Praxis.Modules.Assessment.Tests
git commit -m "The core asks for the shape the pack declared, and banks a coin's odds as a coin's odds (M17)"
```

---

### Task 8: A learner answers by choosing, and the item is served with its choices

**Files:**
- Modify: `src/Modules/Praxis.Modules.Assessment/Features/Diagnostics/DiagnosticResponses.cs`
- Modify: `src/Modules/Praxis.Modules.Assessment/Features/Diagnostics/SubmitAnswer.cs`
- Test: `tests/Praxis.Modules.Assessment.Tests/SubmitAnswerTests.cs`

**Interfaces:**
- Consumes: `BankedItem.Format/Passage/Options` (Task 3), `CheckClaimRequest.SelectedOption` (Task 7's contract change — add it there if Task 7 has not).
- Produces: `SubmitAnswer.Command(Guid SessionId, Guid ItemId, string? Claim, double StatedConfidence, int Seconds, int? SelectedOption = null)`; `NextItemResponse(..., string? Passage, IReadOnlyList<string>? Options, string? Format)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Praxis.Modules.Assessment.Tests/SubmitAnswerTests.cs`:

```csharp
[Fact]
public async Task Choosing_the_option_the_oracle_marks_correct_records_a_right_answer()
{
    var item = await BankFixedKeyItem();
    var ai = new StubAiPlane(ClaimOutcome.Correct, misconception: null);

    var result = await HandlerWith(ai).HandleAsync(
        new SubmitAnswer.Command(SessionId, item.Id, Claim: null, 0.85, 30, SelectedOption: 1),
        default);

    Assert.True(result.IsSuccess);
    Assert.Equal("Correct", result.Value.Outcome);
    Assert.Equal(1, ai.LastCheck!.SelectedOption);
    Assert.Equal(string.Empty, ai.LastCheck.Claim);
}

[Fact]
public async Task A_distractor_records_the_named_belief_with_the_chosen_option_as_evidence()
{
    var item = await BankFixedKeyItem();
    var ai = new StubAiPlane(ClaimOutcome.Wrong, misconception: "keyword-match-wins");

    await HandlerWith(ai).HandleAsync(
        new SubmitAnswer.Command(SessionId, item.Id, Claim: null, 0.85, 30, SelectedOption: 0),
        default);

    var detected = await EventsFor(LearnerId).OfType<MisconceptionDetected>().SingleAsync();

    Assert.Equal("keyword-match-wins", detected.Key);

    // The evidence is what they actually did: the option they picked, in their words as shown.
    Assert.Equal("Everyone who worked a Saturday shift last month.", detected.Evidence);
}

[Fact]
public async Task Prose_on_a_fixed_key_item_is_refused_and_nothing_is_appended()
{
    var item = await BankFixedKeyItem();

    var result = await HandlerWith(new StubAiPlane(ClaimOutcome.Correct, null)).HandleAsync(
        new SubmitAnswer.Command(SessionId, item.Id, Claim: "I think it is B", 0.85, 30, null),
        default);

    Assert.False(result.IsSuccess);
    Assert.Empty(await EventsFor(LearnerId).ToListAsync());
}

[Fact]
public async Task An_option_outside_the_ones_shown_is_refused()
{
    var item = await BankFixedKeyItem();   // four options

    var result = await HandlerWith(new StubAiPlane(ClaimOutcome.Correct, null)).HandleAsync(
        new SubmitAnswer.Command(SessionId, item.Id, Claim: null, 0.85, 30, SelectedOption: 4),
        default);

    Assert.False(result.IsSuccess);
}

[Fact]
public async Task A_selection_on_a_prose_item_is_refused()
{
    var item = await BankProseItem();

    var result = await HandlerWith(new StubAiPlane(ClaimOutcome.Correct, null)).HandleAsync(
        new SubmitAnswer.Command(SessionId, item.Id, Claim: null, 0.85, 30, SelectedOption: 0),
        default);

    Assert.False(result.IsSuccess);
}
```

Add to the validator tests in the same file:

```csharp
[Theory]
[InlineData(null, null)]
[InlineData("words", 1)]
public void An_answer_is_words_or_a_choice_and_never_both_or_neither(string? claim, int? option)
{
    var result = new SubmitAnswer.Validator().Validate(
        new SubmitAnswer.Command(Guid.NewGuid(), Guid.NewGuid(), claim, 0.7, 10, option));

    Assert.False(result.IsValid);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Praxis.Modules.Assessment.Tests --nologo --filter "FullyQualifiedName~SubmitAnswer"`
Expected: FAIL — `SubmitAnswer.Command` takes five parameters and `Claim` is not nullable.

- [ ] **Step 3: Widen the command and its validator**

In `SubmitAnswer.cs`, change `string Claim` to `string? Claim` and append:

```csharp
        /// The option they chose, for a fixed-key item. **Exactly one** of this and `Claim` arrives:
        /// an answer is words or a choice, and PRAXIS is told which rather than inferring it.
        int? SelectedOption = null);
```

Replace the `Claim` rule in `Validator`:

```csharp
            RuleFor(c => c.Claim)
                .MaximumLength(20_000);

            RuleFor(c => c.SelectedOption)
                .GreaterThanOrEqualTo(0)
                .When(c => c.SelectedOption.HasValue);

            RuleFor(c => c)
                .Must(c => string.IsNullOrWhiteSpace(c.Claim) != (c.SelectedOption is null))
                .WithMessage(
                    "An answer is either words or a choice. Both, or neither, is a client that "
                    + "does not know what it is submitting — and PRAXIS will not guess which half "
                    + "of it to mark a person on.");
```

- [ ] **Step 4: Branch the handler**

After the `item is null` check in `HandleAsync`, add the shape check:

```csharp
            var chose = command.SelectedOption is not null;

            if (chose != (item.Format is ItemFormat.MultipleChoice))
            {
                return Result<AnswerResponse>.Invalid(
                    "That answer is the wrong shape for this item. A fixed-key item is answered by "
                    + "choosing; a prose item is answered in words. PRAXIS does not translate "
                    + "between them, because the translation would be a guess about a person.");
            }

            if (command.SelectedOption is { } picked
                && (item.Options is null || picked >= item.Options.Count))
            {
                return Result<AnswerResponse>.Invalid("That option was not one of the ones shown.");
            }
```

Pass the selection to the oracle:

```csharp
            var checkedClaim = await _ai.CheckClaimAsync(
                new CheckClaimRequest(
                    ItemId: item.OracleId ?? item.Id.ToString(),
                    Claim: command.Claim ?? string.Empty,
                    LearnerId: learner.ToString(),
                    ConceptId: item.ConceptId.ToString(),
                    SelectedOption: command.SelectedOption),
                ct);
```

And make the misconception's evidence the option they actually picked:

```csharp
                var evidence = command.SelectedOption is { } index && item.Options is not null
                    ? item.Options[index]
                    : command.Claim ?? string.Empty;

                facts.Add(
                    new MisconceptionDetected(Guid.CreateVersion7(), key, Evidence: evidence),
                    _clock.UtcNow,
                    item.ConceptId);
```

- [ ] **Step 5: Serve the shape**

In `DiagnosticResponses.cs`, add three parameters to `NextItemResponse` (after `EvidenceType`):

```csharp
    /// The text under study, for a reading item — null for everything else.
    string? Passage,

    /// The choices, in the order they must be shown. Null for a prose item.
    ///
    /// There is no index here, no `correct`, and no `answer`. The handler that built this record
    /// never had one and could not have obtained one.
    IReadOnlyList<string>? Options,

    /// "Prose" or "MultipleChoice" — how the client should ask for an answer.
    string? Format,
```

Fill them in `Ask` (`Passage: item.Passage, Options: item.Options, Format: item.Format.ToString()`) and pass `null` for all three in `Stop`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --nologo`
Expected: PASS across the whole solution.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Praxis.Modules.Assessment tests/Praxis.Modules.Assessment.Tests
git commit -m "An answer is words or a choice, and PRAXIS is told which (M17)"
```

---

### Task 9: The tutor is given the passage, and has nowhere to put an option

**Files:**
- Create: `src/Modules/Praxis.Modules.Tutor/ITutorContext.cs`
- Create: `src/Praxis.Api/Composition/TutorContext.cs`
- Modify: `src/Praxis.Infrastructure/Ai/AiPlaneContract.cs:76-96`
- Modify: `src/Praxis.Api/Program.cs:70` (registration)
- Modify: `src/Modules/Praxis.Modules.Tutor/Features/Sessions/SessionLifecycle.cs:15-60`
- Modify: `src/Modules/Praxis.Modules.Tutor/Features/Sessions/TakeHint.cs:166-180`
- Modify: `src/Modules/Praxis.Modules.Tutor/Features/Sessions/StreamHint.cs:150-164`
- Test: `tests/Praxis.Modules.Tutor.Tests/TakeHintTests.cs`, and the AI plane's jailbreak suite

**Interfaces:**
- Consumes: `BankedItem.Passage/Stem` (Task 3) and `Concept.Summary` via a new port.
- Produces: `TutorTurnRequest(..., string? Passage = null)`; `Praxis.Modules.Tutor.ITutorContext` with `Task<TutorItemContext?> ForItemAsync(Guid itemId, CancellationToken ct = default)` and `Task<string?> SummaryForAsync(Guid conceptId, CancellationToken ct = default)`, where `TutorItemContext(string Stem, string? Passage)`; `StartSession.Command(Guid ConceptId, int PlannedMinutes, Guid? ItemId = null)`.

**Note on the port:** the Tutor module today owns exactly one port, `IMisconceptionLibrary`, and it has no way to reach a concept's summary or an item — which is why both call sites send placeholders. `ITutorContext` is that missing seam, and it is one port rather than two because both questions have the same answer: *what may the tutor know about what the learner is looking at?*

- [ ] **Step 1: Write the failing tests**

Add to `tests/Praxis.Modules.Tutor.Tests/TakeHintTests.cs`:

```csharp
[Fact]
public void The_tutor_request_has_no_field_an_option_could_arrive_in()
{
    // The refusal is enforced by the *type*, not by an instruction in a prompt. If somebody adds
    // `Options` here for a perfectly good reason, this fails before it ships.
    var forbidden = typeof(TutorTurnRequest)
        .GetProperties()
        .Where(p => p.Name.Contains("option", StringComparison.OrdinalIgnoreCase)
                 || p.Name.Contains("choice", StringComparison.OrdinalIgnoreCase)
                 || p.Name.Contains("distractor", StringComparison.OrdinalIgnoreCase))
        .Select(p => p.Name)
        .ToList();

    Assert.True(
        forbidden.Count == 0,
        $"""
         The tutor's request grew a field the options could travel in: {string.Join(", ", forbidden)}

         The four choices are a closed set containing the answer. A model that can see them can
         eliminate them, and "it is not B or D" is the answer delivered slowly. The passage is
         safe because the learner is reading it; the options are not, and the reason this holds is
         that there is nowhere to put them.
         """);
}

[Fact]
public async Task A_session_bound_to_an_item_hands_the_tutor_the_real_question_and_its_passage()
{
    var ai = new CapturingAiPlane();
    var items = new StubTutorContext(
        item: new TutorItemContext(
            Stem: "Whose roster is the system most likely to get wrong?",
            Passage: "The new roster system goes live on Monday. However, if you swapped a shift..."),
        summary: "Connectives that reverse the claim before them.");

    var session = await StartSessionOn(ItemId);

    await HandlerWith(ai, items).HandleAsync(
        new TakeHint.Command(session.Id, "Orient", "I think it is everyone"), default);

    Assert.Equal("Whose roster is the system most likely to get wrong?", ai.LastTurn!.ItemStem);
    Assert.Contains("However", ai.LastTurn.Passage);

    // The placeholders are gone: a Socratic tutor working from a GUID and the words "the current
    // problem" was not a tutor.
    Assert.DoesNotContain("the current problem", ai.LastTurn.ItemStem, StringComparison.Ordinal);
    Assert.False(Guid.TryParse(ai.LastTurn.ConceptSummary, out _));
}

[Fact]
public async Task An_unbound_session_still_works_and_sends_no_passage()
{
    var ai = new CapturingAiPlane();
    var session = await StartSessionOn(itemId: null);

    await HandlerWith(ai, new StubTutorContext(item: null, summary: "A concept summary.")).HandleAsync(
        new TakeHint.Command(session.Id, "Orient", "something"), default);

    Assert.Null(ai.LastTurn!.Passage);
    Assert.Equal("the current problem", ai.LastTurn.ItemStem);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Praxis.Modules.Tutor.Tests --nologo`
Expected: FAIL — `TutorTurnRequest` has no `Passage`, `IItemContext` does not exist.

- [ ] **Step 3: Widen the tutor request**

In `AiPlaneContract.cs`, add to `TutorTurnRequest` after `ItemStem`:

```csharp
    /// The passage the learner is reading, when the item has one. **They are looking at it**, so a
    /// hint that points into it ("what does *however* in sentence two reverse?") is teaching, not
    /// answering.
    ///
    /// There is no `Options` beside this, and there must never be. The choices are a closed set
    /// containing the answer: a model that can see them can eliminate them, and elimination *is*
    /// the answer, delivered politely. The learner sees the options; the tutor does not.
    string? Passage = null,
```

Place it before `Transcript` only if you also update the two positional call sites; simplest is to append it **last and defaulted** and pass it by name.

- [ ] **Step 4: Add the port and bind the session**

Create `src/Modules/Praxis.Modules.Tutor/ITutorContext.cs`:

```csharp
namespace Praxis.Modules.Tutor;

/// What the tutor may know about what the learner is looking at.
///
/// A port, like `IMisconceptionLibrary`: Tutor names what it needs, the composition root decides
/// where it comes from, and no module reads another module's tables. Until M17 the Tutor had no
/// such seam at all, which is why both hint paths sent a raw GUID as the "concept summary" and the
/// literal words "the current problem" as the question. A Socratic tutor working from those is not
/// a tutor; it is a well-mannered stranger.
///
/// Note the shape. A summary, a stem, and a passage — **every one of which the learner can already
/// see**. There is no options field and no answer field, so an adapter has nothing to over-supply
/// even if a future one wanted to.
public sealed record TutorItemContext(string Stem, string? Passage);

public interface ITutorContext
{
    /// Null when the item is unknown, or when the session is not bound to one — the caller carries
    /// on without it rather than inventing one.
    Task<TutorItemContext?> ForItemAsync(Guid itemId, CancellationToken ct = default);

    /// The concept's summary in words. Null when the Graph has never heard of it.
    Task<string?> SummaryForAsync(Guid conceptId, CancellationToken ct = default);
}
```

Add `Guid? ItemId = null` to the start-session `Command` in `SessionLifecycle.cs`, store it on the session entity, and add a migration:

```bash
dotnet ef migrations add SessionItemId --project src/Modules/Praxis.Modules.Tutor --startup-project src/Praxis.Api
```

Create the adapter `src/Praxis.Api/Composition/TutorContext.cs`, mirroring `GraphConceptIntel`. It is the one place allowed to hold both contexts, because that is what a composition root is for:

```csharp
public sealed class TutorContext : ITutorContext
{
    private readonly AssessmentDbContext _items;
    private readonly GraphDbContext _graph;

    public TutorContext(AssessmentDbContext items, GraphDbContext graph)
        => (_items, _graph) = (items, graph);

    public async Task<TutorItemContext?> ForItemAsync(Guid itemId, CancellationToken ct = default)
        => await _items.Items.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => new TutorItemContext(i.Stem, i.Passage))
            .FirstOrDefaultAsync(ct);

    public async Task<string?> SummaryForAsync(Guid conceptId, CancellationToken ct = default)
        => await _graph.Concepts.AsNoTracking()
            .Where(c => c.Id == conceptId)
            .Select(c => c.Summary)
            .FirstOrDefaultAsync(ct);
}
```

Register it in `Program.cs` beside the `IConceptIntel` registration:

```csharp
builder.Services.AddScoped<Praxis.Modules.Tutor.ITutorContext, TutorContext>();
```

- [ ] **Step 5: Replace the placeholders**

In both `TakeHint.cs` and `StreamHint.cs`, inject `ITutorContext` as `_context` and resolve both halves before the turn is assembled. The summary falls back to the GUID only if the Graph has genuinely never heard of the concept, which is a bug elsewhere and should look like one:

```csharp
            var summary = await _context.SummaryForAsync(session.ConceptId, ct)
                ?? session.ConceptId.ToString();

            var itemContext = session.ItemId is { } boundItem
                ? await _context.ForItemAsync(boundItem, ct)
                : null;

            var reply = await _ai.TutorTurnAsync(
                new TutorTurnRequest(
                    ConceptSummary: summary,
                    ItemStem: itemContext?.Stem ?? "the current problem",
                    Transcript: ...,      // unchanged
                    Rung: ...,            // unchanged
                    BudgetRemaining: ...,
                    KnownMisconceptions: known,
                    LearnerId: learner.ToString(),
                    ConceptId: session.ConceptId.ToString(),
                    Passage: itemContext?.Passage),
                ct);
```

Keep `"the current problem"` as the fallback for an unbound session — it is honest there, because there genuinely is no item.

- [ ] **Step 6: Re-run the jailbreak suite against the stream**

Find the AI plane's jailbreak tests and add a passage-bearing case to **both** the buffered turn and the streaming turn: feed a passage plus a transcript in which the learner demands the answer, and assert the reply contains none of the four option strings — which the tutor was never given, so this is a regression test on the absence, not on the model's manners. A stream has no schema; the input shape is the only thing standing there, which is why it must be tested separately rather than assumed to carry over.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --nologo` then `cd ai && uv run pytest -q`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Praxis.Modules.Tutor src/Praxis.Infrastructure/Ai/AiPlaneContract.cs src/Praxis.Api tests/Praxis.Modules.Tutor.Tests ai/tests
git commit -m "The tutor is handed the passage and the real question, and has nowhere to put an option (M17)"
```

---

### Task 10: The screen — a passage to read and four choices to make

**Files:**
- Modify: `web/lib/api.ts:142-171`
- Modify: `web/lib/diagnostic.ts`
- Modify: `web/app/diagnostic/page.tsx:142-228`
- Test: `web/lib/diagnostic.test.ts`, `web/app/diagnostic/page.test.tsx`

**Interfaces:**
- Consumes: `NextItemResponse.Passage/Options/Format` and `AnswerRequest.selectedOption` from Task 8.
- Produces: `answerIsReady(state: { claim: string; selectedOption: number | null }, format: string | null): boolean` in `web/lib/diagnostic.ts`.

- [ ] **Step 1: Write the failing tests**

Add to `web/lib/diagnostic.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { answerIsReady } from "./diagnostic";

describe("answerIsReady", () => {
  it("a fixed-key item needs a choice and ignores typed words", () => {
    expect(answerIsReady({ claim: "", selectedOption: 2 }, "MultipleChoice")).toBe(true);
    expect(answerIsReady({ claim: "some words", selectedOption: null }, "MultipleChoice")).toBe(false);
  });

  it("option zero is a real choice, not a missing one", () => {
    // The bug this test exists to prevent: `if (selectedOption)` treats A as unanswered.
    expect(answerIsReady({ claim: "", selectedOption: 0 }, "MultipleChoice")).toBe(true);
  });

  it("a prose item needs words", () => {
    expect(answerIsReady({ claim: "because however reverses it", selectedOption: null }, "Prose")).toBe(true);
    expect(answerIsReady({ claim: "   ", selectedOption: null }, "Prose")).toBe(false);
  });
});
```

Add to `web/app/diagnostic/page.test.tsx` (follow the file's existing mocking of `@/lib/api`):

```tsx
const READING_ITEM = {
  itemId: "i1",
  stem: "Whose roster is the system most likely to get wrong?",
  evidenceType: "Apply",
  passage: "The system goes live Monday.\n\nHowever, if you swapped a shift, it has no record.",
  options: [
    "Everyone who worked a Saturday shift last month.",
    "Anyone who swapped a shift without telling the office.",
    "All warehouse staff.",
    "Anyone who calls the front desk.",
  ],
  format: "MultipleChoice",
  theta: 0,
  standardError: 1,
  confidentEnoughToStop: false,
  stopped: false,
  says: null,
};

it("renders the passage with its paragraphs intact", async () => {
  nextItem.mockResolvedValue({ ok: true, data: READING_ITEM });
  render(<DiagnosticPage />);

  expect(await screen.findByText(/goes live Monday/)).toBeInTheDocument();
  expect(screen.getByText(/However, if you swapped/)).toBeInTheDocument();
});

it("offers the four choices as radios and nothing to type", async () => {
  nextItem.mockResolvedValue({ ok: true, data: READING_ITEM });
  render(<DiagnosticPage />);

  expect(await screen.findAllByRole("radio")).toHaveLength(4);
  expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
});

it("will not submit until a choice is made", async () => {
  nextItem.mockResolvedValue({ ok: true, data: READING_ITEM });
  render(<DiagnosticPage />);

  const submit = await screen.findByRole("button", { name: /submit/i });
  expect(submit).toBeDisabled();

  await userEvent.click(screen.getAllByRole("radio")[0]);
  expect(submit).toBeEnabled();
});

it("sends the chosen index and no claim", async () => {
  nextItem.mockResolvedValue({ ok: true, data: READING_ITEM });
  submitAnswer.mockResolvedValue({
    ok: true,
    data: { outcome: "Wrong", misconception: "keyword-match-wins", youSaidYouWereSure: true, andYouWere: "not" },
  });
  render(<DiagnosticPage />);

  await userEvent.click((await screen.findAllByRole("radio"))[0]);
  await userEvent.click(screen.getByRole("button", { name: /submit/i }));

  expect(submitAnswer).toHaveBeenCalledWith(
    expect.anything(),
    expect.objectContaining({ itemId: "i1", selectedOption: 0 }),
  );
  expect(submitAnswer.mock.calls[0][1].claim).toBeUndefined();
});

it("never shows which option was right", async () => {
  nextItem.mockResolvedValue({ ok: true, data: READING_ITEM });
  submitAnswer.mockResolvedValue({
    ok: true,
    data: { outcome: "Wrong", misconception: "keyword-match-wins", youSaidYouWereSure: true, andYouWere: "not" },
  });
  render(<DiagnosticPage />);

  await userEvent.click((await screen.findAllByRole("radio"))[0]);
  await userEvent.click(screen.getByRole("button", { name: /submit/i }));

  // The key never reached this browser, so there is nothing here that could name it.
  expect(await screen.findByText(/keyword-match-wins/)).toBeInTheDocument();
  expect(screen.queryByText(/correct answer/i)).not.toBeInTheDocument();
  expect(screen.queryByText(/the right answer/i)).not.toBeInTheDocument();
});

it("still renders the typing box for a prose item", async () => {
  nextItem.mockResolvedValue({
    ok: true,
    data: { ...READING_ITEM, passage: null, options: null, format: "Prose" },
  });
  render(<DiagnosticPage />);

  expect(await screen.findByRole("textbox")).toBeInTheDocument();
  expect(screen.queryAllByRole("radio")).toHaveLength(0);
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd web && npx vitest run lib/diagnostic.test.ts app/diagnostic/page.test.tsx`
Expected: FAIL — `answerIsReady` is not exported; no radios render.

- [ ] **Step 3: Widen the types**

In `web/lib/api.ts`, add to `NextItemResponse`:

```ts
  /** The text under study, for a reading item. Null for everything else. */
  passage: string | null;
  /** The choices, in the order they must be shown. Null for a prose item — and never an index. */
  options: string[] | null;
  /** "Prose" | "MultipleChoice" — how this item wants to be answered. */
  format: string | null;
```

and change `AnswerRequest`:

```ts
export interface AnswerRequest {
  itemId: string;
  /** The learner's words, for a prose item. Absent when they chose instead. */
  claim?: string;
  /** The index of the option they picked. Absent when they typed instead. */
  selectedOption?: number;
  statedConfidence: number;
  seconds: number;
}
```

- [ ] **Step 4: Add the pure rule**

In `web/lib/diagnostic.ts`:

```ts
/**
 * Whether there is an answer worth submitting.
 *
 * Note the explicit `!== null` on the selection: option A is index 0, and `if (selectedOption)`
 * would treat the first choice as no choice at all — a bug that would look like a broken button
 * to every learner who reads the options top-down and agrees with the first one.
 */
export function answerIsReady(
  state: { claim: string; selectedOption: number | null },
  format: string | null,
): boolean {
  if (format === "MultipleChoice") {
    return state.selectedOption !== null;
  }
  return state.claim.trim() !== "";
}
```

- [ ] **Step 5: Render the passage and the options**

In `web/app/diagnostic/page.tsx`, add `const [selectedOption, setSelectedOption] = useState<number | null>(null);`, reset it wherever `setClaim("")` already happens, and build the request body from the format:

```tsx
    const res = await diagnostics.submitAnswer(session, {
      itemId: state.current.itemId,
      ...(state.current.format === "MultipleChoice"
        ? { selectedOption: selectedOption! }
        : { claim: claim.trim() }),
      statedConfidence: confidence,
      seconds,
    });
```

In `QuestionCard`, render the passage above the stem when present:

```tsx
      {passage ? (
        <>
          <p className="mt-4 text-[11px] uppercase tracking-[0.14em] text-ink-4">The passage</p>
          <div className="mt-2 max-h-[52vh] overflow-y-auto pr-2.5">
            <div className="serif max-w-[66ch] text-[16.5px] leading-[1.75] text-ink">
              {passage.split(/\n\s*\n/).map((para, i) => (
                <p key={i} className="mb-[0.95em] last:mb-0">
                  {para}
                </p>
              ))}
            </div>
          </div>
          <p className="mt-3.5 border-t border-line-soft pt-3 text-[11.5px] leading-relaxed text-ink-4">
            Re-read it as often as you like. Your time is recorded because it is data about you — it
            is not a score, and reading slowly has never been marked down.
          </p>
        </>
      ) : null}
```

and swap the answer control on format. Real radios inside a `fieldset`, so arrow-key navigation, grouping and screen-reader semantics come from the platform rather than from us:

```tsx
      {options ? (
        <fieldset className="mt-4 min-w-0 border-0 p-0">
          <legend className="text-[11px] uppercase tracking-[0.14em] text-ink-4">Choose one</legend>
          {options.map((option, index) => (
            <label key={index} className="relative mt-2 block">
              <input
                type="radio"
                name="option"
                checked={selectedOption === index}
                onChange={() => onSelect(index)}
                className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
              />
              <span
                className={`flex min-h-12 items-start gap-3 rounded-md border px-3.5 py-2.5 ${
                  selectedOption === index
                    ? "border-accent-line bg-[color:var(--accent-soft)]"
                    : "border-line bg-bg"
                }`}
              >
                <span
                  aria-hidden="true"
                  className={`mono mt-px grid h-[22px] w-[22px] shrink-0 place-items-center rounded-full border text-[11px] ${
                    selectedOption === index
                      ? "border-accent bg-accent font-bold text-accent-ink"
                      : "border-line bg-surface text-ink-3"
                  }`}
                >
                  {String.fromCharCode(65 + index)}
                </span>
                <span className={`text-[14px] leading-normal ${selectedOption === index ? "text-ink" : "text-ink-2"}`}>
                  {option}
                </span>
              </span>
            </label>
          ))}
        </fieldset>
      ) : (
        /* the existing label + textarea block, unchanged */
      )}
```

Gate the submit button on `!answerIsReady({ claim, selectedOption }, format)` instead of `claim.trim() === ""`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd web && npm run test && npm run lint && npm run build`
Expected: PASS — including every existing diagnostic test, since a prose item still renders the textarea.

- [ ] **Step 7: Commit**

```bash
git add web/lib web/app/diagnostic
git commit -m "A passage to read and four choices to make, and no screen that names the key (M17)"
```

---

### Task 11: The end-to-end proof, the gate, and the record

**Files:**
- Modify: `tests/Praxis.Api.Tests/ItemBankE2ETests.cs`
- Modify: `PLAN.md`

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Write the failing test**

Add to `tests/Praxis.Api.Tests/ItemBankE2ETests.cs`, following the file's existing `ScriptedAiPlane` idiom:

```csharp
[Fact]
public async Task A_reading_concept_fills_its_bank_serves_a_passage_and_marks_a_distractor_by_name()
{
    var learner = await RegisterAsync();
    var concept = await SeedReadingConceptAsync();   // multiple-choice apply, one declared belief

    // The bank is empty, and the diagnostic says so rather than inventing an item.
    var empty = await Client.GetAsync($"/api/assessment/diagnostics/{Session}/next-item?conceptId={concept}");
    Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);

    var pooled = await PostAsync("/api/assessment/diagnostics/pool", new { conceptId = concept });
    Assert.Equal(5, pooled.Generated);

    var served = await GetAsync<NextItemResponse>(
        $"/api/assessment/diagnostics/{Session}/next-item?conceptId={concept}");

    Assert.Equal("MultipleChoice", served.Format);
    Assert.False(string.IsNullOrWhiteSpace(served.Passage));
    Assert.Equal(4, served.Options!.Count);

    var marked = await PostAsync<AnswerResponse>(
        $"/api/assessment/diagnostics/{Session}/answers",
        new { itemId = served.ItemId, selectedOption = 0, statedConfidence = 0.85, seconds = 30 });

    Assert.Equal("Wrong", marked.Outcome);
    Assert.Equal("keyword-match-wins", marked.Misconception);

    // The adversarial sweep, on *property names* — the same structural check M14.7 established,
    // because a declared wrong belief's free text may legitimately contain the word "answer".
    foreach (var captured in Ai.EverythingSentAndReceived)
    {
        foreach (var property in PropertyNamesIn(captured))
        {
            Assert.DoesNotContain("correct_option", property, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("correct_index", property, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("answer", property, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

If `PropertyNamesIn` already exists in this file under another name, use that one.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Praxis.Api.Tests --nologo --filter "FullyQualifiedName~ItemBankE2E"`
Expected: FAIL — the scripted AI plane returns a prose item with no options.

- [ ] **Step 3: Make it pass**

Extend `ScriptedAiPlane` so a `GenerateItemRequest` carrying `ItemFormat.MultipleChoice` returns an `ItemResponse` with a passage and four options, and so `CheckClaimAsync` honours `SelectedOption` (index 1 correct; index 0 returns `keyword-match-wins`). Add `SeedReadingConceptAsync` beside the file's existing seeding helper, giving the concept a multiple-choice Apply clause and one declared misconception.

- [ ] **Step 4: Run the whole gate**

Run: `pwsh scripts/verify.ps1`
Expected: every stage PASS — `.NET build`, `.NET test`, `AI lint`, `AI test`, `Web lint`, `Web test`, `Web build`. A SKIP is not a PASS; if a stage skips, find out why before continuing.

- [ ] **Step 5: Load the pack into the dev database and serve one real item**

```bash
dotnet run --project src/Praxis.Api -- --load-pack packs/english-reading/pack.yaml
```

Then, with `ANTHROPIC_API_KEY` set in `ai/.env` and the AI plane running, top up a pool for one reading concept and fetch `next-item` once. Confirm by eye that a real passage and four real options come back, and that no field named `correct`, `answer`, or `key` appears in the JSON. Record what you saw in the PLAN entry — this is the only step in the milestone that puts a live model behind the shape.

- [ ] **Step 6: Write the record**

Add to `PLAN.md` after the M16 section:

```markdown
## M17 — The fixed-key reading item

> The reading pack described a marking scheme the product did not have. This builds it. Spec:
> `docs/superpowers/specs/2026-07-26-fixed-key-reading-item-design.md` · plan:
> `docs/superpowers/plans/2026-07-26-fixed-key-reading-item.md`.

- [x] **M17.1** A passage, four choices, and a key the core cannot reach.
      → [one line on what actually landed, with the test counts from the gate]
```

Update the **Status** block's "Last completed task" line in the same style as the entries above it, and state plainly that the reading item is reachable through the API and tests but **not yet by clicking through the browser** — the onboarding wiring and sign-in are M20.

- [ ] **Step 7: Commit**

```bash
git add tests/Praxis.Api.Tests PLAN.md
git commit -m "The reading item, proven end to end and on the record (M17)"
```

---

## Notes for whoever executes this

- **Tasks 1–2 are .NET+pack, 4–6 are Python, 7–9 are .NET, 10 is web.** Tasks 4–6 depend only on each other and can be done by someone who never opens a `.cs` file; they must land before Task 7, because Task 7's tests exercise the widened wire.
- **Every new parameter added to an existing record or method is last and defaulted.** That is not stylistic — it is what keeps the existing call sites compiling and keeps every already-banked item meaning exactly what it meant yesterday.
- **If a test in this plan disagrees with the code you find**, the code you find wins on *names* and the plan wins on *behaviour*. Adjust the test to the real helper names in the file you are editing; do not weaken what it asserts.
- **`SchemaTests` failing is never fixed by editing `SchemaTests`.** If a column you added trips it, the column is wrong.
