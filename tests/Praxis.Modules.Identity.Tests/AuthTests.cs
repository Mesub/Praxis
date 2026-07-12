using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Praxis.Infrastructure.Web;
using Praxis.Modules.Identity.Features.Auth;
using Praxis.Shared;

namespace Praxis.Modules.Identity.Tests;

[Collection(SharedIdentity.Name)]
public sealed class AuthTests
{
    private readonly IdentityFixture _fixture;

    public AuthTests(IdentityFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Registering_creates_a_learner_and_returns_a_token()
    {
        await using var db = _fixture.CreateContext();
        var handler = Register(db);

        var result = await handler.HandleAsync(
            new RegisterLearner.Command("Ada@example.com", "Ada", "correct horse battery"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ada@example.com", result.Value.Email);
        Assert.NotEmpty(result.Value.AccessToken);

        var stored = await db.Learners.SingleAsync(l => l.NormalisedEmail == "ADA@EXAMPLE.COM");
        Assert.Equal("Ada", stored.DisplayName);
    }

    [Fact]
    public async Task The_password_is_never_stored_in_the_clear()
    {
        const string password = "correct horse battery staple";

        await using var db = _fixture.CreateContext();

        await Register(db).HandleAsync(
            new RegisterLearner.Command("hash@example.com", "Hash", password),
            CancellationToken.None);

        var stored = await db.Learners.SingleAsync(l => l.NormalisedEmail == "HASH@EXAMPLE.COM");

        Assert.DoesNotContain(password, stored.PasswordHash, StringComparison.Ordinal);
        Assert.True(stored.PasswordHash.Length > 40, "That does not look like a hash.");
    }

    [Fact]
    public async Task An_email_can_only_be_registered_once_regardless_of_casing()
    {
        await using var db = _fixture.CreateContext();
        var handler = Register(db);

        await handler.HandleAsync(
            new RegisterLearner.Command("dupe@example.com", "First", "a passphrase here"),
            CancellationToken.None);

        var second = await handler.HandleAsync(
            new RegisterLearner.Command("DUPE@Example.COM", "Second", "another passphrase"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, second.Status);
    }

    [Fact]
    public async Task Logging_in_with_the_right_password_returns_a_token()
    {
        await using var db = _fixture.CreateContext();

        await Register(db).HandleAsync(
            new RegisterLearner.Command("login@example.com", "Login", "a good long passphrase"),
            CancellationToken.None);

        var result = await Login(db).HandleAsync(
            new LoginLearner.Command("login@example.com", "a good long passphrase"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.AccessToken);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_are_refused_identically()
    {
        // Different messages would tell an attacker which addresses are registered.
        await using var db = _fixture.CreateContext();

        await Register(db).HandleAsync(
            new RegisterLearner.Command("known@example.com", "Known", "a good long passphrase"),
            CancellationToken.None);

        var wrongPassword = await Login(db).HandleAsync(
            new LoginLearner.Command("known@example.com", "not the passphrase"),
            CancellationToken.None);

        var unknownEmail = await Login(db).HandleAsync(
            new LoginLearner.Command("nobody@example.com", "not the passphrase"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, wrongPassword.Status);
        Assert.Equal(ResultStatus.Unauthorized, unknownEmail.Status);
        Assert.Equal(wrongPassword.Message, unknownEmail.Message);
    }

    [Fact]
    public async Task Logging_in_records_when()
    {
        var clock = new FakeClock();

        await using var db = _fixture.CreateContext();

        await Register(db).HandleAsync(
            new RegisterLearner.Command("when@example.com", "When", "a good long passphrase"),
            CancellationToken.None);

        await Login(db, clock).HandleAsync(
            new LoginLearner.Command("when@example.com", "a good long passphrase"),
            CancellationToken.None);

        var stored = await db.Learners.SingleAsync(l => l.NormalisedEmail == "WHEN@EXAMPLE.COM");

        Assert.Equal(clock.UtcNow, stored.LastLoginAt);
    }

    [Fact]
    public async Task The_token_carries_the_learner_id_the_rest_of_the_system_reads()
    {
        await using var db = _fixture.CreateContext();

        var registered = await Register(db).HandleAsync(
            new RegisterLearner.Command("claims@example.com", "Claims", "a good long passphrase"),
            CancellationToken.None);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(registered.Value.AccessToken);

        var learnerId = jwt.Claims.Single(c => c.Type == HttpUserContext.LearnerIdClaim).Value;

        Assert.Equal(registered.Value.LearnerId.ToString(), learnerId);
    }

    [Fact]
    public void A_token_expires()
    {
        var clock = new FakeClock();
        var tokens = _fixture.Tokens(clock);

        var learner = Domain.Learner.Register("exp@example.com", "Exp", "hash");

        var issued = tokens.IssueFor(learner);

        Assert.Equal(clock.UtcNow.AddMinutes(_fixture.Jwt.LifetimeMinutes), issued.ExpiresAt);
    }

    [Fact]
    public void A_signing_key_that_is_missing_or_too_short_is_refused_at_startup()
    {
        // HS256 will sign with a 4-byte key and never complain. Better to fail loudly here than
        // to issue forgeable tokens quietly.
        var missing = Assert.Throws<InvalidOperationException>(
            () => new Auth.TokenService(new Auth.JwtOptions { SigningKey = "" }, new FakeClock()));

        Assert.Contains("no default", missing.Message, StringComparison.OrdinalIgnoreCase);

        var tooShort = Assert.Throws<InvalidOperationException>(
            () => new Auth.TokenService(new Auth.JwtOptions { SigningKey = "short" }, new FakeClock()));

        Assert.Contains("at least", tooShort.Message, StringComparison.OrdinalIgnoreCase);
    }

    private RegisterLearner.Handler Register(Persistence.IdentityDbContext db)
        => new(db, _fixture.Hasher, _fixture.Tokens(new SystemClock()));

    private LoginLearner.Handler Login(Persistence.IdentityDbContext db, IClock? clock = null)
        => new(db, _fixture.Hasher, _fixture.Tokens(new SystemClock()), clock ?? new SystemClock());
}
