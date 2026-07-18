# M14 final-review fix report

## Finding (Important) — Key-length hole between Graph and Assessment

A pack could declare a misconception key up to 120 chars (the loader's charset check in
`PackValidator.IsWellFormedKey` had no length rule, and `GraphDbContext` allows `HasMaxLength(120)`
on `ConceptMisconception.Key`), but Assessment's learner-model columns
(`BankedItem.TargetsMisconception`, `LearnerMisconception.Key`) are `HasMaxLength(64)`. M14 wired
the first automated path that copies a Graph misconception key into those 64-char columns: a key of
65–120 chars was valid at pack load, crossed the wire, survived the generator's airlock, and then
blew up `SaveChangesAsync` in `EnsureConceptPool` with a value-too-long error — losing the whole
generated batch, silently, every time (the web pool call is best-effort and swallows the 500).

## Fix

Added a maximum-length rule (≤ 64 chars) to `PackValidator`, next to the existing kebab-case check,
so an over-long key is refused at pack load with an honest message instead of surviving to blow up
the database write. Assessment's columns were left untouched (they are the correct limit; Graph was
the one lying about what it would accept).

**File:** `src/Modules/Praxis.Modules.Graph/Packs/PackValidator.cs`

- Added `private const int MaxMisconceptionKeyLength = 64;` at the top of the class, with a comment
  naming the two Assessment columns it must match and why (`EnsureConceptPool` failure mode).
- In `ValidateMisconceptions`, added a check alongside the existing `IsWellFormedKey` check:
  if `m.Key.Trim().Length > MaxMisconceptionKeyLength`, add an error naming the concept's slug, the
  offending key, its actual length, the 64-char limit, and the two columns it must fit
  (`BankedItem.TargetsMisconception`, `LearnerMisconception.Key`).

No change to `GraphDbContext` (still `HasMaxLength(120)` at the database layer — the pack validator
is now the gate that keeps a key from ever reaching that far) and no change to Assessment's columns,
per the reviewer's chosen fix.

## TDD

Followed the existing `PackValidatorTests.cs` style (raw YAML string literals via `Parse(...)`,
`Assert.Contains(result.Errors, e => e.Contains(...))`).

Added two tests to `tests/Praxis.Modules.Graph.Tests/PackValidatorTests.cs`:

1. `A_misconception_key_longer_than_64_characters_is_refused` — a 65-char key
   (`new string('a', 65)`) on concept `a`; asserts `ResultStatus.Invalid` and an error containing
   `"cannot exceed 64 characters"`.
2. `A_misconception_key_of_exactly_64_characters_passes` — boundary case, a 64-char key
   (`new string('a', 64)`); asserts `result.IsSuccess`.

**Red step (before the fix):** ran the covering test file —
`A_misconception_key_longer_than_64_characters_is_refused` failed as expected
(`Expected: Invalid / Actual: Success`); `A_misconception_key_of_exactly_64_characters_passes`
already passed (boundary is naturally fine pre-fix, confirming the test isolates the >64 case only).

**Green step (after the fix):** both tests pass; all 17 tests in `PackValidatorTests` pass.

## Commands run

1. `dotnet test tests\Praxis.Modules.Graph.Tests\Praxis.Modules.Graph.Tests.csproj --filter "FullyQualifiedName~PackValidatorTests" --nologo -v normal`
   — red: 16 passed, 1 failed (`A_misconception_key_longer_than_64_characters_is_refused`).
   — green (after fix): **17 passed, 0 failed.**
2. `dotnet test Praxis.slnx --nologo -v quiet`
   — **all 13 test projects passed** (~1085 tests total: Kernel 49, Shared 29, Infrastructure 21,
     Identity 9, Memory 54, Compiler 65, Tutor 57, Graph 96, Assessment 158, Analytics 34,
     Scheduler 15, Twin 97, Reality 158, Ledger 70, Api 132 — 0 failed across the board).
3. `docker compose up -d` — `praxis-postgres` already running.
4. `.\scripts\verify.ps1`
   — **PASSED** — every stage ran and passed: .NET build, .NET test, AI lint, AI test, Web lint,
     Web test, Web build.

## Files changed

- `src/Modules/Praxis.Modules.Graph/Packs/PackValidator.cs`
- `tests/Praxis.Modules.Graph.Tests/PackValidatorTests.cs`
