using Praxis.Modules.Analytics.Domain;
using Praxis.Modules.Kernel;
using Praxis.Shared;

namespace Praxis.Modules.Analytics.Features;

/// **Your rate of return on your own attention** — computed, never stored, from the log and the Twin.
///
/// The mastery (numerator) comes from what the Twin has verified; the minutes (denominator) are folded
/// from the immutable event log. It reads the learner off the token — there is no `?learnerId=`,
/// because this is the number that says whether PRAXIS was worth *your* time, and it is not anyone
/// else's to look up.
public static class GetAnalytics
{
    public sealed record Query;

    public sealed class Handler
    {
        private readonly IEventStore _events;
        private readonly IAnalyticsSnapshot _snapshot;
        private readonly IUserContext _user;

        public Handler(IEventStore events, IAnalyticsSnapshot snapshot, IUserContext user)
            => (_events, _snapshot, _user) = (events, snapshot, user);

        public async Task<Result<AnalyticsResponse>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var learner = _user.RequireLearnerId();

            var stream = await _events.ReadStreamAsync(learner, ct: ct);
            var minutes = MinutesInvested.From(stream);

            var mastery = await _snapshot.MasteryCreditByAsync(learner, ct);

            var mpm = MasteryPerMinute.Compute(mastery, minutes);

            return Result<AnalyticsResponse>.Success(new AnalyticsResponse(
                mpm.Rate,
                mpm.MasteryCreditGained,
                mpm.MinutesInvested,
                mpm.HasData,
                mpm.GainedNothing));
        }
    }
}

/// **The whole truth about the trade** — the rate, and the two numbers it is made of, so the screen
/// can be honest (a low rate is shown as a low rate, and a zero is shown as a zero) without doing any
/// arithmetic of its own. `Rate` is null when no minutes have been invested: there is nothing to
/// divide yet, and that is said, not faked.
public sealed record AnalyticsResponse(
    double? Rate,
    double MasteryCredit,
    double MinutesInvested,
    bool HasData,
    bool GainedNothing);
