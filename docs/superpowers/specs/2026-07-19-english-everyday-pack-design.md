# Everyday English — a second domain pack (design)

Date: 2026-07-19 · Status: approved by user (session, after clickable prototype)
Prototype shown: onboarding card → Nepali-tuned article item → misconception-catch verdict.

## Goal

Add English as a real PRAXIS Objective: **write clear, natural everyday English — messages,
plans, opinions — with the errors Nepali speakers actually make hunted down by name.**

Learner profile it is authored for (from the requesting user, generalises to the cohort):
intermediate level, Nepali first language, everyday-fluency goal. Written English only —
PRAXIS cannot grade speech in Phase 0, and we say so rather than pretend (C-1 discipline).

## Scope

- `packs/english-everyday/pack.yaml` — ~30 concepts, `requires`/`contrasts` edges,
  Nepali-transfer misconceptions, proof contracts. Loaded via `dotnet run -- --load-pack`.
- One onboarding card in `web/lib/onboarding.ts` (`OBJECTIVES`): "Everyday English", Serious.
- A loader test proving the shipped pack passes the gate (mirrors ai-engineering's coverage).
- **No engine changes.** The diagnostic, generator, misconception hunter, tutor and memory
  all operate on this pack unchanged — that is the entire point of packs.

Out of scope: speaking/listening, exam prep, login UI, i18n of the console itself.
Prerequisite for live practice (not for this change): `ANTHROPIC_API_KEY` in `ai/.env`.

## The map (7 regions, ~30 concepts)

Regions and the `requires` spine (full list in the pack itself):

1. **Sentence machinery** — word-order-svo → subject-verb-agreement, questions-do-support,
   negation-do. Nepali is SOV and marks questions by tone; do-support is the wall.
2. **Articles & nouns** (the big Nepali gap — Nepali has no articles) — a-an-first-mention →
   the-definite-reference → zero-article-generalities; countable-uncountable → quantifiers.
3. **Tenses as Nepali speakers meet them** — present-simple-vs-continuous → past-simple →
   present-perfect-vs-past; future-forms; past-continuous.
4. **Prepositions & verb patterns** — in-on-at-time → in-on-at-place → verb-preposition-pairs;
   gerund-vs-infinitive → phrasal-verbs-core; modals (ability → advice/obligation).
5. **Vocabulary in use** — collocations-make-do-take-have → word-families;
   nepali-english-false-friends.
6. **Connected writing** — linking-words → relative-clauses → conditionals-real-unreal →
   reported-speech-basics.
7. **Real writing** — formal-vs-informal → everyday-messages → short-emails.

Cross-region edges: word-order-svo feeds articles and tenses; the-definite-reference feeds
collocations; present-perfect feeds linking-words; verb-preposition-pairs feeds
everyday-messages. `contrasts` edges carry the confusable pairs (present-perfect ↔ past-simple,
a-an ↔ the, much ↔ many) — that is where the Assessment engine hunts distractors.

## Misconceptions (Nepali transfer, ~15, one home concept each)

Keys are global, kebab-case, ≤64 chars (PackValidator enforces all three). The list, with the
belief stated in the learner's own voice:

| key | home concept | the belief |
|---|---|---|
| articles-are-decoration | a-an-first-mention | "a/an/the are polish; the sentence is fine without them" |
| the-marks-importance | the-definite-reference | "'the' makes a noun important, not known-to-both" |
| continuous-shows-politeness | present-simple-vs-continuous | "'I am knowing' sounds more polite/correct" |
| perfect-is-fancy-past | present-perfect-vs-past | "'have + verb' is a fancier way to say the past" |
| prepositions-translate-literally | verb-preposition-pairs | "the English preposition is the Nepali postposition translated" |
| one-future-will | future-forms | "'will' is the future tense; the rest are variations" |
| uncountables-take-plural | countable-uncountable | "advices, informations — more of it means add -s" |
| questions-by-tone-alone | questions-do-support | "rising tone makes it a question; 'do' is optional" |
| much-many-interchangeable | quantifiers | "much/many mean the same; pick by feel" |
| agreement-is-optional | subject-verb-agreement | "he go / he goes — same meaning, both fine" |
| gerund-infinitive-same | gerund-vs-infinitive | "enjoy to swim / enjoy swimming — interchangeable" |
| formal-is-always-better | formal-vs-informal | "the most formal phrasing is always the safest" |
| word-order-flexible | word-order-svo | "English order can bend the way Nepali does" |
| collocation-any-verb | collocations-make-do-take-have | "make/do/take/have are interchangeable helpers" |
| double-past-did | past-simple | "did + went — both past-marked, extra correct" |

## Proof contracts

Same C-4 arithmetic every pack obeys (enforced by `PoMContract.Create` at load):

- Every concept: **Apply is the floor-carrier** (max ≥ hold) — never-seen sentence tasks:
  fill the gap, fix the sentence, choose what a fluent writer would send, write the line.
- Vocab-flavoured concepts add Recall (max ≤ 0.15); rule-flavoured add Explain (max ≤ 0.20);
  misconception-heavy add Critique (max ≤ 0.15) — model-graded sums stay < hold everywhere.
- Holds 0.75–0.80; the two writing concepts 0.80 with Apply 0.80.
- No Build/Predict: the Reality Loop is code-only in Phase 0, and a prose task cannot run
  against hidden tests. Apply items are oracle-marked, which is the unfakeable path language has.

Difficulty priors: −1.2 (word-order-svo, easy for an intermediate learner) to +1.6
(reported-speech, register nuance). Minutes: 25–60 per concept. All priors, replaced by cohort
measurement, as ever.

## Onboarding card

`OBJECTIVES` entry: id `obj-everyday-english`, title "Everyday English", difficulty "Serious",
role/cost lines per the approved prototype, keywords ("english", "grammar", "writing",
"vocabulary", "articles", "tenses", "fluency", "esl"). Existing card tests extended.

## Verification

- Loader gate: new test in the Graph test project loads `packs/english-everyday/pack.yaml`
  from disk (same root-walk as `ItemBankE2ETests.SeedPack`) and asserts success — proving
  every contract passes ceiling+floor and the graph is acyclic, orphan-free, dangling-free.
- Web: onboarding tests cover the new card (search finds "english"; card renders).
- Full gate `scripts/verify.ps1` green; pack loaded into the dev DB via `--load-pack`.

## Decisions taken (overridable)

- American spelling in summaries and items.
- 30 concepts, not exhaustive coverage — shortest honest path; v2 grows it with cohort data.
- One pack, not per-skill packs — one Objective compiles one path.
