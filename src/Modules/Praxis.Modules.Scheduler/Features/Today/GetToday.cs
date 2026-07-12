using FluentValidation;
using Praxis.Modules.Scheduler.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Scheduler.Features.Today;

/// **The whole product, in one request.**
///
/// *"I have fifteen minutes. What do I do?"*
///
/// Everything else PRAXIS builds — the graph, the twin, the forgetting curve, the compiler — exists
/// so that this one endpoint can answer that question with **one card** instead of a dashboard.
public static class GetToday
{
    /// The scheduling atom. Not a free-form integer: a learner asked to *specify* their available
    /// minutes is being asked to plan, and planning is the thing they came here to stop doing.
    public static readonly int[] SessionLengths = [5, 10, 15, 25];

    public sealed record Query(int Minutes = 15);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
            => RuleFor(q => q.Minutes)
                .Must(m => SessionLengths.Contains(m))
                .WithMessage("A session is 5, 10, 15 or 25 minutes. These are the sizes a real day "
                             + "has room for — and a text box asking how long you have is just "
                             + "planning by another name.");
    }

    public sealed class Handler
    {
        private readonly ISchedulerSnapshot _snapshot;
        private readonly IUserContext _user;

        public Handler(ISchedulerSnapshot snapshot, IUserContext user)
            => (_snapshot, _user) = (snapshot, user);

        public async Task<Result<TodayCardResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var inputs = await _snapshot.ForLearnerAsync(_user.RequireLearnerId(), query.Minutes, ct);

            if (!inputs.IsSuccess)
            {
                return inputs.CarryFailure<TodayCardResponse>();
            }

            return Result<TodayCardResponse>.Success(
                TodayCardResponse.From(TodayScheduler.Choose(inputs.Value)));
        }
    }
}

public sealed record TodayCardResponse(
    string Kind,
    Guid ConceptId,
    string Slug,
    string Headline,
    string Because,
    int Minutes,

    /// Everything that lost. Its job is to make the decision **auditable**, not to be shown — the
    /// moment the card becomes a list, PRAXIS has handed the hardest moment in learning (*starting*)
    /// straight back to the person who is demonstrably bad at it.
    IReadOnlyList<DeferredResponse> AlsoDue)
{
    public static TodayCardResponse From(TodayCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new TodayCardResponse(
            card.Kind.ToString(),
            card.ConceptId,
            card.Slug,
            card.Headline,
            card.Because,
            card.Minutes,
            [.. card.AlsoDue.Select(d => new DeferredResponse(d.ConceptId, d.Slug, d.Reason, d.Minutes))]);
    }
}

public sealed record DeferredResponse(Guid ConceptId, string Slug, string Reason, int Minutes);
