using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Praxis.Infrastructure.Ai;
using Praxis.Modules.Assessment.Domain;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Features.Diagnostics;

/// **A thin pool is topped up with items whose answers this process never saw.**
///
/// The adaptive diagnostic (`NextItem`) can only serve what is already banked, and it refuses rather
/// than invents when the bank is thin (M13.1). This slice is the other half of that refusal: it is
/// the thing that keeps the bank from staying thin. It never reads an answer, never writes one, and
/// never touches the event log — it only decides *how many more items the bank needs for this
/// learner* and asks the generator for exactly that many. The answer key it generates goes to the
/// oracle's store on the far side of the AI-plane wire and never comes back here (M5.5).
///
/// **Novelty is checked against the whole bank, not just this learner.** A duplicate stem would be
/// the same item wearing a new Guid, and the unique index on `Fingerprint` would refuse it at
/// `SaveChangesAsync` — but by then a paid generation call has already been spent on nothing. Passing
/// every banked fingerprint as `AlreadySeen` means it never gets that far.
public static class EnsureConceptPool
{
    public sealed record Command(Guid ConceptId);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator() => RuleFor(c => c.ConceptId).NotEmpty();
    }

    public sealed record PoolResponse(int Generated, int PoolSize);

    /// How many unseen items a learner's diagnostic should have waiting. Below this, PRAXIS
    /// writes more; at it, calling this endpoint is free and does nothing.
    public const int PoolFloor = 5;

    public sealed class Handler
    {
        private readonly AssessmentDbContext _db;
        private readonly IAiPlane _ai;
        private readonly IConceptIntel _intel;
        private readonly IUserContext _user;

        public Handler(AssessmentDbContext db, IAiPlane ai, IConceptIntel intel, IUserContext user)
            => (_db, _ai, _intel, _user) = (db, ai, intel, user);

        public async Task<Result<PoolResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            var intel = await _intel.ForAsync(command.ConceptId, ct);
            if (intel is null)
            {
                return Result<PoolResponse>.NotFound(
                    "No such concept. PRAXIS does not write items about things the Graph has "
                    + "never heard of.");
            }

            var seenByLearner = await _db.Responses.AsNoTracking()
                .Where(r => r.LearnerId == learner && r.ConceptId == command.ConceptId)
                .Select(r => r.ItemId).Distinct().ToListAsync(ct);

            var banked = await _db.Items.AsNoTracking()
                .Where(i => i.ConceptId == command.ConceptId)
                .Select(i => new { i.Id, i.Fingerprint }).ToListAsync(ct);

            var unseen = banked.Count(i => !seenByLearner.Contains(i.Id));
            if (unseen >= PoolFloor)
            {
                return Result<PoolResponse>.Success(new PoolResponse(0, unseen));
            }

            // Every fingerprint already in the bank: novelty against the whole bank, not just this
            // learner — a duplicate stem would be the same item wearing a new Guid.
            var fingerprints = banked.Select(i => i.Fingerprint).ToList();

            var generated = new List<BankedItem>();
            Result<PoolResponse>? failed = null;

            for (var needed = PoolFloor - unseen; generated.Count < needed;)
            {
                var response = await _ai.GenerateItemAsync(
                    new GenerateItemRequest(
                        ConceptId: command.ConceptId.ToString(),
                        ConceptSummary: intel.Summary,
                        EvidenceType: EvidenceType.Apply,
                        Difficulty: intel.DifficultyTarget,
                        LearnerId: learner.ToString(),
                        AlreadySeen: fingerprints,
                        KnownMisconceptions: intel.DeclaredMisconceptions,
                        ItemFormat: intel.ApplyFormat),
                    ct);

                if (!response.IsSuccess)
                {
                    // Real items already made are kept; the failure is only fatal when it left
                    // nothing behind. A learner is never refused because the *sixth* item failed.
                    failed = response.CarryFailure<PoolResponse>();
                    break;
                }

                var item = response.Value;
                fingerprints.Add(item.Fingerprint);

                generated.Add(BankedItem.From(
                    Guid.CreateVersion7(),
                    command.ConceptId,
                    item.EvidenceType,
                    item.Stem,
                    item.Fingerprint,
                    // Generator 0..1 → IRT logits. The echo is clamped first: a buggy AI plane
                    // must not be able to write a wild logit the estimator would then trust.
                    difficulty: (Math.Clamp(item.Difficulty, 0.0, 1.0) * 6.0) - 3.0,
                    // **A coin has a θ too.** An N-way choice is right 1/N of the time whatever the
                    // learner knows, and the 3PL estimator must be told so — an item banked at zero
                    // reads a lucky guess as evidence and inflates everyone who takes it.
                    guessing: item.Options is { Count: > 0 } shown ? 1.0 / shown.Count : 0.0,
                    targetsMisconception: item.TargetsMisconception,
                    oracleId: item.Id,
                    format: intel.ApplyFormat,
                    passage: item.Passage,
                    options: item.Options));
            }

            if (generated.Count == 0 && failed is not null)
            {
                return failed;
            }

            _db.Items.AddRange(generated);

            try
            {
                await _db.SaveChangesAsync(ct);   // the one save
            }
            catch (DbUpdateException collision) when (
                collision.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // `AlreadySeen` should make this unreachable — but that guarantee lives on the far
                // side of the wire. A plane that echoes a banked fingerprint anyway, or a race with
                // a concurrent top-up, is a conflict the caller can retry, not a bug in this handler.
                return Result<PoolResponse>.Conflict(
                    "An item with this fingerprint is already banked. The pool may have been "
                    + "topped up concurrently — ask again.");
            }

            return Result<PoolResponse>.Success(
                new PoolResponse(generated.Count, unseen + generated.Count));
        }
    }
}
