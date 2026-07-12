using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Twin.Persistence;
using Praxis.Shared;
using Testcontainers.PostgreSql;

namespace Praxis.Modules.Twin.Tests;

public sealed class TwinFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("praxis_test")
            .WithUsername("praxis")
            .WithPassword("praxis_test_only")
            .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public TwinDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TwinDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", TwinDbContext.Schema))
            .Options;

        return new TwinDbContext(options);
    }
}

[CollectionDefinition(SharedTwin.Name)]
public sealed class SharedTwin : ICollectionFixture<TwinFixture>
{
    public const string Name = "twin";
}

/// Decay is the domain. A Twin that cannot be tested without waiting 90 days is a Twin nobody
/// tests.
public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
