# Reading English — a third domain pack (design)

Date: 2026-07-24 · Status: approved by user (session, after clickable prototype)
Prototype shown: onboarding card (with Listening as a greyed sibling) → main-idea item with
keyword-trap distractors → tutor hint that does not know the answer key (C-5 demonstrated live).

## Goal

Add English *reading comprehension* as a real PRAXIS Objective: **understand everyday and work
English at reading speed — get the point, catch the implication, and trust the right answer over
the one that merely repeats the words — with the reading habits a Nepali reader carries hunted
down by name.**

This is the receiving-side twin of the shipped `english-everyday` pack. That pack teaches a
learner to *put English out*; this one teaches them to *take it in*. Same world, opposite
direction. Same learner profile: intermediate, Nepali first language, everyday-fluency goal.

## Scope

Ships (mirrors the `english-everyday` change exactly):

- `packs/english-reading/pack.yaml` — 15 concepts across 3 regions, `requires`/`contrasts`
  edges, reading-habit misconceptions, Apply-floor proof contracts. Loaded via
  `dotnet run -- --load-pack`.
- One onboarding card in `web/lib/onboarding.ts` (`OBJECTIVES`): "Reading English", Serious.
- A loader test in the Graph test project proving the shipped pack passes the gate (mirrors
  `EnglishPackTests`).
- **No engine changes.** The diagnostic, generator, misconception hunter, tutor and memory all
  operate on this pack unchanged — that is the entire point of packs.

Material is **everyday + work** English: news stories, opinion pieces, work emails and chat
threads, instructions, product/notice text. No academic/exam corpus, no technical-doc corpus in
v1.

Out of scope: **listening** (deferred — see below), speaking, exam prep, login UI, i18n of the
console. Prerequisite for live practice (not for this change): `ANTHROPIC_API_KEY` in `ai/.env`.

### Listening is a named follow-up, not a silent omission (M17)

Reading needs nothing new — a passage is text. **Listening needs audio, and Praxis has none:**
no text-to-speech, no media storage, no player, anywhere in the codebase. Rather than smuggle
that in or fake it with transcripts (which would be exactly the pretending C-1 exists to stop),
listening is deferred to its own honest design: **M17 — Listening English**, which adds TTS
generation, audio storage, and a web player, and ships as this pack's twin. The pack is named
**"Reading English"** (not "Comprehension") precisely so "Listening English" can be its sibling
in the catalog rather than a bolt-on.

## The map (3 regions, 15 concepts)

Comprehension fails for two different reasons — the sentence trips you, or you parse every word
and still miss the point — so the pack has a bottom-up half and a top-down half, then a payoff
region where both meet real material.

**Region A — Decoding the sentence (the obstacles, bottom-up):**

1. `long-noun-phrases` — stacked nouns ("customer service response time targets"); find the head.
2. `embedded-clauses` — relative/subordinate clauses that split subject from verb.
3. `reference-tracking` — what `it / they / this / the former` point back to.
4. `connectives-that-flip` — however, although, whereas, nonetheless: meaning reversal, not filler.
5. `phrasal-verbs-in-context` — untranslatable; infer from context (leans on the writing pack).
6. `what-the-article-signals` — the *reading* side of articles: `a` = new, `the` = you should
   already know this. Mirror of the writing pack's `a-an-first-mention` (prose note, not an edge).

**Region B — Getting the meaning (the strategies, top-down):**

7. `gist-vs-detail` — read for the point vs scan for a fact; match effort to purpose.
8. `main-idea` — the point of a paragraph / whole text vs supporting detail.
9. `inference` — what is implied but unstated; the safe inference vs the overreach.
10. `authors-stance` — for / against / neutral; fact vs opinion.
11. `vocab-from-context` — infer an unknown word from its neighbours, without a dictionary.
12. `reading-tone-register` — formal/informal, warm/curt, and what it reveals about intent.
    Mirror of the writing pack's `formal-vs-informal` (prose note, not an edge).

**Region C — Real-world receiving (the payoff):**

13. `reading-a-work-email` — subject, ask, deadline: what action is actually required of you.
14. `following-instructions` — steps, conditions, order; "if X then Y" logic in prose.
15. `news-vs-opinion` — a fact reported vs a claim argued.

### The `requires` spine and cross-region edges

Region A supplies the atoms. `embedded-clauses` → `gist-vs-detail` (you can't skim what you can't
parse); `reference-tracking` and `connectives-that-flip` → `inference`; `what-the-article-signals`
→ `main-idea`. In B, `gist-vs-detail` → `main-idea` → `inference` → `authors-stance`;
`gist-vs-detail` → `vocab-from-context`; `authors-stance` → `reading-tone-register`. Into C:
`main-idea` → `reading-a-work-email`; `connectives-that-flip` → `following-instructions`;
`inference` + `authors-stance` → `news-vs-opinion`.

`contrasts` edges carry the confusable pairs the Assessment engine mines for distractors:
`main-idea ↔ supporting-detail` (as gist-vs-detail), `inference ↔ literal statement`,
`fact ↔ opinion` (at authors-stance).

**All `requires` edges stay inside this pack** — one Objective compiles one path, and cross-pack
`requires` is not a thing packs support. The two mirror concepts (`what-the-article-signals`,
`reading-tone-register`) are related to writing-pack concepts in *prose only*, never wired as
cross-pack edges.

## Misconceptions (reading habits, one home concept each)

Same discipline as the writing pack — name the wrong belief so the item generator hunts it
(M14.2) and the Twin watches it die — but here they are reading-strategy transfer errors, several
of them the classic exam-distractor traps. Keys are global, kebab-case, ≤64 chars.

| key | home concept | the belief |
|---|---|---|
| every-word-or-fail | gist-vs-detail | "if I don't know every word, I haven't understood the passage" |
| dictionary-reflex | vocab-from-context | "an unknown word means stop and look it up" |
| keyword-match-wins | main-idea | "the option that repeats words from the text is the answer" |
| literal-only | inference | "only what's printed is true; I can't safely conclude more" |
| nearest-noun-reference | reference-tracking | "'it / they / this' means the closest noun before it" |
| connectives-are-filler | connectives-that-flip | "however / although are decoration; skip to the facts" |
| fact-and-opinion-same | authors-stance | "if it's written confidently, it's a fact" |
| head-is-first-noun | long-noun-phrases | "the first noun in a stack is what the phrase is about" |

## Proof contracts

Same C-4 arithmetic every pack obeys (enforced by `PoMContract.Create` at load).

**Apply is the floor-carrier on every concept** (max ≥ hold). For reading, Apply means a
**never-seen passage answered against a fixed key** — and this is arguably Praxis's *purest*
Apply, because the key is set at authoring time and marked deterministically, no model's opinion
anywhere. The oracle-marked Apply forms:

- **Multiple choice** with a fixed correct option.
- **True / False / Not-Given** (IELTS-style) — deterministic.
- **Span selection** — "quote the phrase that says when the meeting is" — exact match to a fixed span.
- **Ordering** — put these events in the order the text implies — deterministic.

This lands the tutor refusal (C-5) cleanly: the **passage** is in the tutor's context (the learner
reads it and may discuss it), but the **answer key is never** — the tutor strategises ("what does
'it' in line 3 point back to?") without knowing which option is correct. A jailbreak retrieves
nothing because nothing is there.

Ceiling evidence (accelerates, can never cross HOLD alone):

- **Explain** (max ≤ 0.20) on the strategy concepts — main-idea, inference, authors-stance:
  "explain why B is the main point."
- **Critique** (max ≤ 0.15) on the misconception-heavy concepts — "here is a wrong answer someone
  chose and their reasoning; find the flaw." Directly exercises the keyword-trap habits.
- **No Recall** — comprehension has little to memorise; nothing here is a fact to key-match.
- **No Build / Predict** — the Reality Loop is code-only in Phase 0, and a comprehension answer
  cannot run against hidden tests.

Holds 0.75–0.80 (the payoff Region C concepts 0.80 with Apply 0.80). Difficulty priors −0.8
(`gist-vs-detail`, easy for an intermediate reader) to +1.5 (`inference`, `authors-stance` —
the overreach/underreach line is genuinely hard). Minutes 25–55 per concept. All priors, replaced
by cohort measurement, as ever.

## Onboarding card

`OBJECTIVES` entry: id `obj-reading-english`, title "Reading English", difficulty "Serious",
role line per the approved prototype, keywords
(`["reading", "comprehension", "understanding", "english", "inference", "skimming", "esl",
"work email", "language"]`). Existing card/search tests extended to cover it.

## Verification

- Loader gate: new test in the Graph test project loads `packs/english-reading/pack.yaml` from
  disk (same root-walk as `EnglishPackTests`/`ItemBankE2ETests.SeedPack`) and asserts success —
  proving every contract passes ceiling+floor and the graph is acyclic, orphan-free, dangling-free.
- Web: onboarding tests cover the new card (search finds "reading"; card renders).
- Full gate `scripts/verify.ps1` green; pack loaded into the dev DB via `--load-pack`.

## Decisions taken (overridable)

- American spelling in summaries and items.
- 15 concepts, not exhaustive — shortest honest path for a receiving-side v1; grows with cohort data.
- One pack "Reading English", not a combined "Comprehension" pack — listening is a named M17
  follow-up because it needs real audio infrastructure that deserves its own design.
- Passages are authored/generated everyday-and-work material; no academic or technical-doc corpus in v1.
