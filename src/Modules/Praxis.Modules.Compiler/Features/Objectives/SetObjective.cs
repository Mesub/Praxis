using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Compiler.Domain;
using Praxis.Modules.Compiler.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Compiler.Features.Objectives;

/// **The learner says what they want to become, and how much of their life they will actually give
/// to it.**
///
/// The second number is the one that matters, and it is the one everybody lies about. A learner who
/// says 90 and does 20 is not committed — they are being lied to by their own optimism, and a
/// product that compiles against the 90 becomes complicit in it. So the number they give here is
/// taken *literally*, and the Decay Wall is run against it (M4.3). If it does not buy what they
/// asked for, they will be told before they start, not eighteen months in.
public static class SetObjective
{
    public sealed record Command(Guid ConceptId, int MinutesPerDay, DateTime? Deadline);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.ConceptId).NotEmpty();

            RuleFor(c => c.MinutesPerDay)
                .GreaterThan(0)
                .WithMessage("An objective with no minutes behind it is a wish. PRAXIS compiles "
                             + "plans, not wishes.")
                .LessThanOrEqualTo(600)
                .WithMessage("Ten hours a day, every day, is not a pace — it is a fortnight of "
                             + "enthusiasm followed by nothing. Give the number you will still be "
                             + "giving in March.");
        }
    }

    public sealed class Handler
    {
        private readonly CompilerDbContext _db;
        private readonly IUserContext _user;

        public Handler(CompilerDbContext db, IUserContext user) => (_db, _user) = (db, user);

        public async Task<Result<ObjectiveResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var learner = _user.RequireLearnerId();

            // One objective at a time. A learner pursuing three things is pursuing none of them, and
            // the Scheduler would have to pick between them anyway — badly, and without saying so.
            var existing = await _db.Objectives
                .Where(o => o.LearnerId == learner && o.IsActive)
                .ToListAsync(ct);

            foreach (var old in existing)
            {
                old.Abandon();
            }

            var objective = Objective.Set(learner, command.ConceptId, command.MinutesPerDay, command.Deadline);

            _db.Objectives.Add(objective);

            await _db.SaveChangesAsync(ct);

            return Result<ObjectiveResponse>.Success(new ObjectiveResponse(
                objective.Id,
                objective.ConceptId,
                objective.MinutesPerDay,
                objective.Deadline,
                Replaced: existing.Count));
        }
    }
}

public sealed record ObjectiveResponse(
    Guid Id,
    Guid ConceptId,
    int MinutesPerDay,
    DateTime? Deadline,

    /// How many objectives this one retired. Usually zero; one when they changed their mind, which
    /// is allowed and is not a failure.
    int Replaced);
