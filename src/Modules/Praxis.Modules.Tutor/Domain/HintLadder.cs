using Praxis.Modules.Kernel.Events;
using Praxis.Shared;

namespace Praxis.Modules.Tutor.Domain;

/// **The hint ladder, as a policy rather than as a prompt.**
///
/// The tutor is a language model, and a language model asked to "be Socratic and not give the
/// answer" will be Socratic for about four turns and then, faced with a frustrated human, help. That
/// is not a flaw in the model; it is the model doing what it was trained to do — be useful to the
/// person in front of it, right now. It is *we* who know that the most useful thing for that person,
/// over the eighteen months they will not remember this conversation, is to not be helped yet.
///
/// So the ladder is not advice in a system prompt. It is a **deterministic gate** the tutor's
/// request has to pass through, and it is enforced here, in code, where no amount of frustration can
/// argue with it.
///
/// ## The rules (CANON §1.2)
///
/// * **LR-1 — No skipping.** Rung *n* is unreachable until rung *n−1* has been served *and the
///   learner has responded to it*. A hint they never engaged with cannot be escalated past.
/// * **LR-2 — Re-request surcharge.** Asking for the same rung again on the same item costs 1.5×.
/// * **LR-3 — Response required.** The tutor may not take two turns in a row. The ladder does not
///   move without a learner utterance.
/// * **LR-4 — Descent is free.** Dropping *back down* the ladder costs nothing, ever. **Backing off
///   is never punished** — a tutor that is charged for retreating will not retreat.
/// * **LR-5 — There is no bottom rung.** `FULL_REVEAL` is not gated, or flagged, or expensive. It
///   does not exist (see `HintRung`). Running out of budget does not get you the answer; it gets you
///   rescheduled.
public static class HintLadder
{
    /// Can the tutor serve this rung, and what does it cost?
    ///
    /// **Everything is a `Result`.** A refusal here is a normal, expected, frequent outcome — it is
    /// the ladder working — and it must never be an exception that some `catch` block upstream can
    /// swallow into "well, just help them then".
    public static Result<HintGrant> Consider(HintRung requested, LadderState state, AnswerBudget budget)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(budget);

        // ── LR-3. The ladder does not move without the learner ───────────────
        //
        // Checked first, because it is the rule that stops the tutor monologuing a learner all the
        // way down the ladder while they sit there reading. A hint delivered to someone who has not
        // yet said anything is not scaffolding — it is a lecture with a price tag.
        if (state.AwaitingLearnerResponse)
        {
            return Result<HintGrant>.Invalid(
                "The learner has not responded to the last hint. The ladder does not advance on the "
                + "tutor's say-so — every turn ends with something for them to do, and nothing "
                + "happens until they do it.");
        }

        // ── LR-4. Descent is free, and always allowed ────────────────────────
        //
        // Before the budget check, deliberately: a learner at zero budget must still be able to be
        // walked *back up* to an orienting question. Charging for a retreat produces a tutor that
        // never retreats, and a tutor that never retreats is one that only ever escalates.
        if (state.DeepestServed is { } deepest && requested < deepest)
        {
            return Result<HintGrant>.Success(
                new HintGrant(requested, Cost: 0, IsDescent: true, IsRepeat: false));
        }

        // ── LR-1. No skipping ────────────────────────────────────────────────
        var next = state.DeepestServed is { } served ? served + 1 : HintRung.Orient;

        if (requested > next)
        {
            return Result<HintGrant>.Invalid(
                $"{requested} cannot be served yet: {next} has not been. A learner who skips to the "
                + "scaffold has not been helped past the difficulty — they have been carried over "
                + "it, and they will meet it again on the next item, unchanged.");
        }

        // ── LR-2. The surcharge for asking again ─────────────────────────────
        var repeat = state.TimesServed(requested) > 0;

        var cost = repeat ? HintCost.Repeated(requested) : HintCost.Of(requested);

        // ── The budget. And the rung that is not below it ────────────────────
        if (cost > budget.Remaining)
        {
            // **Not "OK fine, here's the answer."** That path does not exist in this file, and it
            // does not exist anywhere it could be reached from. Zero budget is a *compile signal*:
            // the item stops, the learner writes down where their reasoning broke, and the concept
            // is rescheduled — behind its missing prerequisites, or with a different explanation.
            //
            // Going broke does not get you the answer. It gets you a better plan.
            return Result<HintGrant>.Invalid(
                $"That rung costs {cost} and there is {budget.Remaining} left on this concept. "
                + "PRAXIS has helped as much as it is going to. It is not going to tell you the "
                + "answer — it is going to ask you where your reasoning breaks, and then come back "
                + "to this with something you are missing.",
                [Exhausted]);
        }

        return Result<HintGrant>.Success(
            new HintGrant(requested, cost, IsDescent: false, IsRepeat: repeat));
    }

    /// The marker on a refusal that means *"the budget is gone"* rather than *"not yet"*. The caller
    /// reads it to decide whether to append `AnswerBudgetExhausted` and ask for a stuck note.
    public const string Exhausted = "answer-budget-exhausted";

    /// Did the ladder refuse because the money ran out, or because the learner has not earned the
    /// next rung yet? Two very different conversations.
    public static bool IsExhaustion(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Errors.Contains(Exhausted, StringComparer.Ordinal);
    }
}

/// What the ladder agreed to, and what it will cost.
public sealed record HintGrant(HintRung Rung, int Cost, bool IsDescent, bool IsRepeat);

/// Where this learner is on the ladder **for one item**.
///
/// Per *item*, not per concept: the ladder resets when the problem does, because a learner who was
/// walked to the scaffold on one problem has not thereby been walked to the scaffold on the next
/// one. The budget, by contrast, is per *concept* — and does not reset. That asymmetry is the whole
/// economics: help is cheap to ask for again, and expensive to keep asking for.
public sealed record LadderState(
    HintRung? DeepestServed,

    /// **LR-3.** True when the tutor has spoken and the learner has not yet answered.
    bool AwaitingLearnerResponse,

    /// How many times each rung has been served on this item. Drives the re-request surcharge.
    IReadOnlyDictionary<HintRung, int> Served)
{
    public static LadderState Fresh { get; } =
        new(DeepestServed: null, AwaitingLearnerResponse: false, Served: new Dictionary<HintRung, int>());

    public int TimesServed(HintRung rung) => Served.GetValueOrDefault(rung, 0);

    /// Record a hint the tutor actually gave. Note it sets `AwaitingLearnerResponse` — after any
    /// hint, it is the learner's move.
    public LadderState After(HintGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var served = new Dictionary<HintRung, int>(Served)
        {
            [grant.Rung] = TimesServed(grant.Rung) + 1,
        };

        // A descent does not *lower* the high-water mark. The learner has still seen the deeper
        // rung, and pretending otherwise would let them walk back up it for free, forever.
        var deepest = DeepestServed is { } current && current > grant.Rung ? current : grant.Rung;

        return this with
        {
            DeepestServed = deepest,
            AwaitingLearnerResponse = true,
            Served = served,
        };
    }

    public LadderState AfterLearnerSpoke() => this with { AwaitingLearnerResponse = false };
}
