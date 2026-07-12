using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Identity.Auth;
using Praxis.Modules.Identity.Domain;
using Praxis.Modules.Identity.Persistence;
using Praxis.Shared;

namespace Praxis.Modules.Identity.Features.Auth;

public static class RegisterLearner
{
    public record Command(string Email, string DisplayName, string Password);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
            RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);

            // Length beats complexity theatre. A 12-character passphrase is stronger than
            // "P@ssw0rd!" and people can actually remember it.
            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(12).WithMessage("Use at least 12 characters. A passphrase is fine.")
                .MaximumLength(256);
        }
    }

    public sealed class Handler
    {
        private readonly IdentityDbContext _db;
        private readonly IPasswordHasher<Learner> _hasher;
        private readonly ITokenService _tokens;

        public Handler(IdentityDbContext db, IPasswordHasher<Learner> hasher, ITokenService tokens)
            => (_db, _hasher, _tokens) = (db, hasher, tokens);

        public async Task<Result<AuthResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            var normalised = Learner.Normalise(command.Email);

            if (await _db.Learners.AnyAsync(l => l.NormalisedEmail == normalised, ct))
            {
                return Result<AuthResponse>.Conflict("An account already exists for that email.");
            }

            var learner = Learner.Register(command.Email, command.DisplayName, passwordHash: "pending");
            learner.RehashPassword(_hasher.HashPassword(learner, command.Password));

            _db.Learners.Add(learner);
            await _db.SaveChangesAsync(ct);

            var token = _tokens.IssueFor(learner);

            return Result<AuthResponse>.Success(
                new AuthResponse(learner.Id, learner.Email, learner.DisplayName, token.Token, token.ExpiresAt),
                "Welcome. PRAXIS will tell you when you are failing.");
        }
    }
}

public sealed record AuthResponse(
    Guid LearnerId,
    string Email,
    string DisplayName,
    string AccessToken,
    DateTime ExpiresAt);
