using Praxis.Modules.Kernel.Events;
using Praxis.Modules.Tutor.Domain;

namespace Praxis.Modules.Tutor.Features.Sessions;

/// **What the learner sees, and — as always — what they do not.**
///
/// There is no `Answer` here, and there is no `CorrectAnswer`. Not because the controller declines
/// to populate one, but because the .NET core **does not have one to populate**: the answer lives in
/// the oracle's store, on the far side of a wire whose contract has no field for it (M5.7).
///
/// The budget *is* here, and prominently. **It is their money and they are entitled to watch it.**
/// A scarcity you cannot see is not a scarcity, it is a surprise — and a learner who discovers the
/// limit only at the moment it stops them has been ambushed rather than taught.
public sealed record SessionResponse(
    Guid SessionId,
    Guid ConceptId,
    string State,
    string? Outcome,

    /// **Shown, always.** See above.
    int BudgetRemaining,

    /// What the next rung would cost them, so the decision is an informed one rather than a gamble.
    int NextRungCost,
    string? NextRung,

    int HintsTaken,

    /// Things they have **made**. The number this whole product is about.
    int Produced,

    bool MadeSomething,

    /// True when the tutor has spoken and it is the learner's move. The ladder does not advance
    /// until they say something (LR-3).
    bool AwaitingYou);

/// One turn of the conversation.
public sealed record TurnResponse(
    string Tutor,

    /// The rung this turn was served at, and what it cost. The learner sees the price *after* the
    /// fact as well as before — no silent charges.
    string Rung,
    int Cost,

    SessionResponse Session);

/// **The budget is gone, and the answer is still not coming.**
///
/// This is not an error response. It is the product working. The learner is asked for the most
/// valuable two sentences in the session — *where, exactly, does your reasoning break?* — and the
/// concept is rescheduled around whatever they say.
public sealed record ExhaustedResponse(
    string Message,

    /// The prompt for the stuck note. Mandatory, and the reason the session will not end in
    /// consumption.
    string AskFor,

    SessionResponse Session)
{
    public static ExhaustedResponse For(SessionResponse session) => new(
        "PRAXIS has helped as much as it is going to on this concept. It is not going to tell you "
        + "the answer — running out of budget was never a route to one.",
        "Write two sentences: where exactly does your reasoning break? Not what you don't know — "
        + "where the thing you *do* know stops being enough. That note is worth more to your plan "
        + "than the answer would have been, and it is the reason this session will not have been "
        + "wasted.",
        session);
}

public static class SessionMapping
{
    public static SessionResponse ToResponse(this Session session, AnswerBudget budget)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(budget);

        var next = NextRung(session);

        return new SessionResponse(
            SessionId: session.Id,
            ConceptId: session.ConceptId,
            State: session.State.ToString(),
            Outcome: session.Outcome?.ToString(),
            BudgetRemaining: budget.Remaining,
            NextRungCost: next is { } rung ? HintCost.Of(rung) : 0,
            NextRung: next?.ToString(),
            HintsTaken: session.HintsTaken,
            Produced: session.Produced,
            MadeSomething: session.MadeSomething,
            AwaitingYou: session.AwaitingLearner);
    }

    /// The next rung the ladder would allow. `null` at the bottom — **and there is nothing below the
    /// bottom.** The absence of a rung here is the absence of a rung, not a locked one.
    private static HintRung? NextRung(Session session)
    {
        if (session.DeepestRungServed is not { } deepest)
        {
            return HintRung.Orient;
        }

        return deepest == HintRung.PartialScaffold ? null : deepest + 1;
    }
}
