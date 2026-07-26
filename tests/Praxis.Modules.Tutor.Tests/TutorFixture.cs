using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Tutor.Persistence;
using Praxis.Shared;
using Testcontainers.PostgreSql;

namespace Praxis.Modules.Tutor.Tests;

/// A real PostgreSQL, with **both** the kernel's schema and the Tutor's.
///
/// `TakeHint`/`StreamHint` are the busiest slices in the tutor — the ladder, the budget and the
/// event log all have to agree before a single hint is served — and a fake event store would let a
/// test drive the projections by hand, in whatever order the test author chose. That is exactly the
/// thing worth *not* trusting here: M17's claim is that a session bound to an item hands the tutor
/// the real question *because it was written to the log and read back out*, not because a test
/// wired two objects together in the right order.
public sealed class TutorFixture : IAsyncLifetime
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

        await using var kernel = CreateKernel();
        await kernel.Database.MigrateAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public TutorDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TutorDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", TutorDbContext.Schema))
            .Options;

        return new TutorDbContext(options);
    }

    public KernelDbContext CreateKernel()
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", KernelDbContext.Schema))
            .Options;

        return new KernelDbContext(options);
    }
}

[CollectionDefinition(SharedTutor.Name)]
public sealed class SharedTutor : ICollectionFixture<TutorFixture>
{
    public const string Name = "tutor";
}

/// Time is not the domain here, so it does not need to move.
public sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
}

public sealed class FakeUser : IUserContext
{
    public FakeUser(Guid learnerId) => LearnerId = learnerId;

    public Guid? LearnerId { get; }

    public bool IsAuthenticated => true;

    public Guid RequireLearnerId() => LearnerId!.Value;
}
