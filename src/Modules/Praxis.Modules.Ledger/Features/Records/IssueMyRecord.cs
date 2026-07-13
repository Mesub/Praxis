using Praxis.Modules.Ledger.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Features.Records;

/// **Your record, signed on demand — a snapshot of what you hold, right now.**
///
/// It is issued fresh each time, never stored: a stored record is a record that keeps saying you hold
/// something you have since let decay, and the whole point is that a PRAXIS record is *true when
/// signed*. Whoever you hand it to verifies it against the published key; the timestamp inside says
/// when it was true.
///
/// It reads the learner off the token and serves only them — there is no `?learnerId=`, because the
/// most valuable thing a person owns is not something anyone gets to mint on their behalf.
public static class IssueMyRecord
{
    public sealed record Query;

    public sealed class Handler
    {
        private readonly ILedgerSnapshot _snapshot;
        private readonly ILedgerKeys _keys;
        private readonly IUserContext _user;
        private readonly IClock _clock;

        public Handler(ILedgerSnapshot snapshot, ILedgerKeys keys, IUserContext user, IClock clock)
            => (_snapshot, _keys, _user, _clock) = (snapshot, keys, user, clock);

        public async Task<Result<CompetencyRecord>> HandleAsync(Query query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);

            var learner = _user.RequireLearnerId();

            var held = await _snapshot.HeldByAsync(learner, ct);

            // The subject is a label the record is *about*, not a proof of identity — the verifier
            // knows who handed it to them (M10). The signing key never touches this handler; it is
            // asked for one signature and hands back nothing else.
            var record = LedgerIssuer.Issue(
                learner,
                subject: learner.ToString(),
                held,
                _keys.Sign,
                _keys.PublicKey,
                _clock.UtcNow);

            return Result<CompetencyRecord>.Success(record);
        }
    }
}
