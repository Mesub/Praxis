using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Shared;
using Testcontainers.PostgreSql;

namespace Praxis.Modules.Assessment.Tests;

/// A real PostgreSQL, with **both** the kernel's schema and Assessment's.
///
/// The kernel is here because the hunt (`TheHuntTests`) is only worth proving against a **real event
/// store**. A fake one would let the projections be driven by hand, in the order I chose, which is
/// exactly the thing that would hide the bug: PRAXIS's claim is that a wrong answer becomes a named
/// belief *because it was written to the log and read back out*, and a test that skips the log is not
/// testing that claim — it is testing my ability to call two methods in the right order.
public sealed class AssessmentFixture : IAsyncLifetime
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

    public AssessmentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", AssessmentDbContext.Schema))
            .Options;

        return new AssessmentDbContext(options);
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

[CollectionDefinition(SharedAssessment.Name)]
public sealed class SharedAssessment : ICollectionFixture<AssessmentFixture>
{
    public const string Name = "assessment";
}

/// Time is not the domain here, so it does not need to move.
public sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

public sealed class FakeUser : IUserContext
{
    public FakeUser(Guid learnerId) => LearnerId = learnerId;

    public Guid? LearnerId { get; }

    public bool IsAuthenticated => true;

    public Guid RequireLearnerId() => LearnerId!.Value;
}
