# 01 — Vision & the New Category

---

## 1. The problem, stated honestly

Education technology has spent twenty years solving **distribution**. It has not touched
**learning**.

We made lectures free. We made them mobile. We made them searchable, subtitled, 2×-speed,
gamified, and now, chat-enabled. Along the way we produced a generation of people who have
watched a hundred hours of video, hold zero verified skills, and quietly believe the failure is
theirs.

It isn't. The tools were built for the wrong objective.

| The learner's actual question | What today's products answer |
|---|---|
| What should I learn? | Here is a catalog. |
| When should I learn it? | Whenever you like. |
| Why does it matter for me? | *(silence)* |
| How do I learn it correctly? | Watch this. |
| How do I know I understood? | You clicked "complete." |
| Will I remember this in six months? | *(silence)* |
| Can I actually *do* it? | *(silence)* |

Six of the seven questions are unanswered. And the seventh is answered with a lie.

**The lie is the "complete" button.** It is the single most damaging control in educational
software. It converts *exposure* into *achievement* with one click, and it is the reason
someone can finish a 40-hour course and be unable to do a single hour of the job.

---

## 2. What changed

Three things became true at once, and nobody has yet built the product they imply:

1. **A machine can now hold a model of a specific human mind.** Not a "profile" — a
   *simulator*. It can track what you know at concept granularity, watch it decay, and predict
   what you'll fail at tomorrow.
2. **A machine can now teach Socratically at the marginal cost of nothing.** The 1:1 tutor —
   Bloom's two-sigma problem, unsolved for 40 years because tutors don't scale — now scales.
3. **A machine can now grade what actually matters.** Not multiple choice. It can grade an
   *explanation*, a *diagram*, a *codebase*, a *diagnosis*, a *trade thesis*. The things a real
   teacher grades.

Everyone has responded to this by bolting a chatbot onto a course catalog. That is the least
imaginative possible use of the moment. A chatbot on a catalog gives the learner an even faster
way to *not learn* — it produces the answer *for* them, and the illusion of understanding gets
cheaper still.

---

## 3. The reframe

The right unit of the product is not the **course**. It is not the **lesson**, the **video**,
or the **chat turn**.

> **The unit is a piece of evidence that a specific human can now do a specific thing.**

Everything follows from that. If evidence is the unit, then:

- A **catalog** is pointless — content is a *means*, generated on demand to produce evidence.
- **"Complete"** is meaningless — you cannot complete an ability, you can only *demonstrate* it,
  and demonstrations *expire*.
- **Engagement** is a bug — time in the app is cost, not value. The best session is the shortest
  one that produced the evidence.
- **Reality** becomes the highest court — the strongest evidence is not a quiz score. It's that
  you predicted the world, and the world agreed.

---

## 4. The category: **Mastery OS**

PRAXIS is not a better LMS. It is a different kind of object.

An operating system's job is to take a scarce resource (CPU time) and a set of competing
demands (processes), and *schedule them optimally against a goal*. It maintains state, it
manages memory, it kills what isn't working, it protects the user from themselves.

PRAXIS does that for a mind.

| OS concept | PRAXIS |
|---|---|
| Process table | **The Cognitive Twin** — live probabilistic state of everything you know |
| Filesystem | **The Knowledge Graph** — concepts, prerequisites, versions |
| Compiler / `make` | **The Curriculum Compiler** — Objective + Graph + Twin → Plan |
| Cache | What you already know |
| **Cache invalidation** | **Forgetting** |
| Scheduler | What you do in the next 15 minutes |
| Memory manager | Spaced repetition, decay, retrieval |
| Test suite / CI | **Proof-of-Mastery** — nothing ships unless it passes |
| Syscalls | The tutor, the assessor, the reviewer |
| Hardware interrupt | **Reality** — the world resolves your prediction whether you're ready or not |
| Kernel log | **The Ledger** — append-only, signed, verifiable |

The metaphor isn't decoration. It's the architecture. (See `07-system-architecture.md`.)

---

## 5. The one-sentence product

> **PRAXIS compiles what you want to become into what you do in the next fifteen minutes — and
> refuses to believe you learned it until you prove it.**

---

## 6. The thing that makes it undeniable: the Reality Loop

Every learning product on earth grades you against *its own answer key*. PRAXIS grades you
against **the world**.

- You mark today's live chart. **Tomorrow, the market grades you.**
- You diagnose a real (de-identified) case. **The actual outcome grades you.**
- You predict the ruling. **The court grades you.**
- You forecast the quarter. **The filing grades you.**
- You ship the code. **The hidden tests grade you.**

We score it with a **Brier score** and track **calibration** — how well your confidence matches
your correctness — as a first-class metric right next to Mastery. Over a year, a learner
accumulates a *timestamped, immutable, verifiable forecasting track record against reality*.

That record cannot be faked, cannot be crammed for, and cannot be bought. No course completion
certificate in the world means anything next to it. **It is the most honest credential ever
issued**, and it is only possible because we made the world itself the examiner.

---

## 7. The refusals (what we will not build, ever)

These are not "out of scope for v1." These are permanent.

| We will never build | Because |
|---|---|
| A course catalog | Browsing is a substitute for learning. There is nothing to browse. |
| A "mark as complete" button | It is the lie at the center of the industry. |
| An infinite feed | The feed has a bottom. Doomscrolling is not studying. |
| A tutor that gives answers | The AI's job is to make the human produce, not to produce for them. |
| Engagement metrics as goals | We optimize for the learner **leaving** — competent. |
| Selling learner data | The Twin is the most intimate dataset ever collected on a person. It is theirs. |
| Streaks that punish rest | Burnout is a learning failure. The OS protects the learner from itself. |

---

## 8. The measure of success

Not signups. Not DAU. Not hours watched. Not NPS.

> **Did the learner become the thing they said they wanted to become — and can they prove it to
> a stranger?**

We commit to measuring this publicly: a **delayed-retention randomized trial** against a
course-based control, published, with the negative results included. (See `24-monetization-and-gtm.md`.)

If PRAXIS cannot beat a good course on retention at 90 days, it does not deserve to exist.

---

## 9. Who this is for, in one line each

- The 22-year-old who has watched 100 tutorials and cannot build one thing.
- The trader who has read every book and still loses money, because nobody ever graded a
  single one of their actual predictions.
- The professional with 25 minutes a day and a career they need to change in a year.
- The med student who will be responsible for a life and needs to know *exactly* what they
  don't know.
- The parent who is tired of buying courses their child abandons in week two.
- The employer who has never once been able to *verify* a line on a résumé.

---

## 10. The bet

> The last decade of edtech was a bet that **access** was the bottleneck.
> It wasn't. Access is solved and people are still not learning.
>
> The bottleneck is that nothing in a learner's life **models them, schedules them, refuses
> them, tests them against reality, and remembers everything** — the way one great teacher who
> knew you for ten years would.
>
> That teacher was never scalable. Now it is.
> **PRAXIS is the bet that the scarce thing was never content. It was being known.**
