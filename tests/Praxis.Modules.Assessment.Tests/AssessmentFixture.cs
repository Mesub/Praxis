using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Assessment.Persistence;
using Testcontainers.PostgreSql;

namespace Praxis.Modules.Assessment.Tests;

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
}

[CollectionDefinition(SharedAssessment.Name)]
public sealed class SharedAssessment : ICollectionFixture<AssessmentFixture>
{
    public const string Name = "assessment";
}
