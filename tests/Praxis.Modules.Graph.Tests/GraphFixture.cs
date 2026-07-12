using Microsoft.EntityFrameworkCore;
using Praxis.Modules.Graph.Persistence;
using Testcontainers.PostgreSql;

namespace Praxis.Modules.Graph.Tests;

public sealed class GraphFixture : IAsyncLifetime
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

    public GraphDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GraphDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", GraphDbContext.Schema))
            .Options;

        return new GraphDbContext(options);
    }
}

[CollectionDefinition(SharedGraph.Name)]
public sealed class SharedGraph : ICollectionFixture<GraphFixture>
{
    public const string Name = "graph";
}
