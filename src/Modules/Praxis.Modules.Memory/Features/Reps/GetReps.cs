using Praxis.Modules.Memory.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Memory.Features.Reps;

/// **What is worth reviewing today** — and PRAXIS's honest opinion of what everything else would
/// cost.
///
/// Read the response and note what is not on it: there is **no count of how far behind you are**, no
/// streak, and no red badge. The queue is ordered by *what a rep is worth*, not by *what you owe*
/// (M8.2) — because sorting by neglect puts the learner's single worst-value item at the top and
/// calls it the most urgent thing they own.
public static class GetReps
{
    public sealed record Query;

    public sealed class Handler
    {
        private readonly IMemorySnapshot _memory;
        private readonly IUserContext _user;

        public Handler(IMemorySnapshot memory, IUserContext user) => (_memory, _user) = (memory, user);

        public async Task<Result<RepsResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            var learner = _user.RequireLearnerId();

            var held = await _memory.HeldByAsync(learner, ct);

            var plan = Domain.Reps.Build(held);

            return Result<RepsResponse>.Success(new RepsResponse(
                Due: [.. plan.Due.Select(ToResponse)],

                // **Named, counted, and handed back to the plan.** Not hidden — and not dressed up as
                // a three-minute review either, because the learner would fail it and lose an evening
                // they had not planned for.
                Lost: [.. plan.Lost.Select(ToResponse)],

                ExpectedMinutes: Math.Round(plan.ExpectedMinutes, 1),

                // **"Nothing worth doing" is a real answer** (I-10). Not "0 due — keep your streak
                // alive!"
                NothingWorthDoing: plan.NothingWorthDoing,

                AboutWhatYouLost: plan.AboutWhatYouLost));
        }

        private static RepResponse ToResponse(ScoredRep rep)
            => new(
                ConceptId: rep.ConceptId,
                Slug: rep.Slug,
                Urgency: rep.Urgency.ToString(),

                // **Said out loud.** A learner is entitled to know that PRAXIS thinks they would get
                // this right 62% of the time — it is a fact about them, and hiding it behind a red
                // dot is how a study tool becomes a slot machine.
                Retrievability: Math.Round(rep.Retrievability, 2),

                // What it will *actually* cost, averaged over passing and failing it. A queue that
                // quotes three minutes a card and then hands you fifteen is not a schedule, it is a
                // trap.
                ExpectedMinutes: Math.Round(rep.ExpectedMinutes, 1),

                Because: Because(rep));

        private static string Because(ScoredRep rep) => rep.Urgency switch
        {
            RepUrgency.Ripe
                => $"You would get this right about {rep.Retrievability:P0} of the time — which is "
                   + "almost exactly when recalling it is worth the most. Too fresh and it teaches "
                   + "you nothing; too far gone and you will simply fail it.",

            RepUrgency.Rotting
                => $"You are down to {rep.Retrievability:P0} and falling. Every day you leave this, "
                   + "it is worth less *and* you are likelier to fail it — which is the one "
                   + "combination that actually deserves the word urgent.",

            RepUrgency.Lost
                => $"You are at {rep.Retrievability:P0}. PRAXIS is not going to call this a "
                   + "three-minute review, because you would most likely fail it and it would cost "
                   + "you an evening. It goes back into the plan as a re-learn, priced honestly.",

            _ => "Still holding.",
        };
    }
}

/// **The queue, and the honest bill.**
public sealed record RepsResponse(
    IReadOnlyList<RepResponse> Due,

    /// Decayed past the point where a review would work. **Not hidden, and not in the queue.**
    IReadOnlyList<RepResponse> Lost,

    /// The **expected** cost of clearing the queue, not the best case.
    double ExpectedMinutes,

    bool NothingWorthDoing,

    string? AboutWhatYouLost);

public sealed record RepResponse(
    Guid ConceptId,
    string Slug,
    string Urgency,
    double Retrievability,
    double ExpectedMinutes,
    string Because);
