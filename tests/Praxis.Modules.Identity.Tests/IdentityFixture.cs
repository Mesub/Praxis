using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Identity.Auth;
using Praxis.Modules.Identity.Domain;
using Praxis.Modules.Identity.Persistence;
using Praxis.Shared;
using Testcontainers.PostgreSql;

namespace Praxis.Modules.Identity.Tests;

public sealed class IdentityFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("praxis_test")
            .WithUsername("praxis")
            .WithPassword("praxis_test_only")
            .Build();

    public JwtOptions Jwt { get; } = new()
    {
        Issuer = "praxis-test",
        Audience = "praxis-test",
        SigningKey = "test-only-signing-key-at-least-32-bytes-long",
        LifetimeMinutes = 60,
    };

    public IPasswordHasher<Learner> Hasher { get; } = new PasswordHasher<Learner>();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", IdentityDbContext.Schema))
            .Options;

        return new IdentityDbContext(options);
    }

    public TokenService Tokens(IClock clock) => new(Jwt, clock);
}

[CollectionDefinition(SharedIdentity.Name)]
public sealed class SharedIdentity : ICollectionFixture<IdentityFixture>
{
    public const string Name = "identity";
}

/// A clock the tests own, so that token expiry can be tested without waiting an hour.
public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
