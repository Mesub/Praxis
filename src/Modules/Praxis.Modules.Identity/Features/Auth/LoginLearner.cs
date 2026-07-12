using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Identity.Auth;
using Praxis.Modules.Identity.Domain;
using Praxis.Modules.Identity.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Identity.Features.Auth;

public static class LoginLearner
{
    public record Command(string Email, string Password);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().MaximumLength(320);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
        }
    }

    public sealed class Handler
    {
        /// One message for "no such account" and for "wrong password". Distinguishing them tells
        /// an attacker which emails are registered.
        private const string Rejected = "Email or password is incorrect.";

        private readonly IdentityDbContext _db;
        private readonly IPasswordHasher<Learner> _hasher;
        private readonly ITokenService _tokens;
        private readonly IClock _clock;

        public Handler(
            IdentityDbContext db,
            IPasswordHasher<Learner> hasher,
            ITokenService tokens,
            IClock clock)
            => (_db, _hasher, _tokens, _clock) = (db, hasher, tokens, clock);

        public async Task<Result<AuthResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var normalised = Learner.Normalise(command.Email);

            var learner = await _db.Learners
                .FirstOrDefaultAsync(l => l.NormalisedEmail == normalised, ct);

            if (learner is null)
            {
                return Result<AuthResponse>.Unauthorized(Rejected);
            }

            var verdict = _hasher.VerifyHashedPassword(learner, learner.PasswordHash, command.Password);

            if (verdict == PasswordVerificationResult.Failed)
            {
                return Result<AuthResponse>.Unauthorized(Rejected);
            }

            // The hasher's work factor rises over time. When it does, an old hash is upgraded
            // on the next successful login — the one moment we hold the plaintext.
            if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
            {
                learner.RehashPassword(_hasher.HashPassword(learner, command.Password));
            }

            learner.RecordLogin(_clock.UtcNow);
            await _db.SaveChangesAsync(ct);

            var token = _tokens.IssueFor(learner);

            return Result<AuthResponse>.Success(
                new AuthResponse(learner.Id, learner.Email, learner.DisplayName, token.Token, token.ExpiresAt));
        }
    }
}
