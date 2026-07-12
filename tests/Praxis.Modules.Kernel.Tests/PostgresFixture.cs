using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Kernel.Persistence;
using Testcontainers.PostgreSql;

namespace Praxis.Modules.Kernel.Tests;

/// A real PostgreSQL, migrations applied.
///
/// Not an in-memory provider, and not a SQLite stand-in. The kernel's central guarantee — that
/// history cannot be edited — is a database trigger. An in-memory fake has no triggers, so it
/// would happily let every one of these tests pass while the real thing was wide open.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("praxis_test")
            .WithUsername("praxis")
            .WithPassword("praxis_test_only")
            .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public KernelDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", KernelDbContext.Schema))
            .Options;

        return new KernelDbContext(options);
    }
}

[CollectionDefinition(SharedPostgres.Name)]
public sealed class SharedPostgres : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
