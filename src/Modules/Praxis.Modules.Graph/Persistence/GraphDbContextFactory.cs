using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Praxis.Modules.Graph.Persistence;

/// Used only by `dotnet ef` at design time. The running app builds its context from DI.
public sealed class GraphDbContextFactory : IDesignTimeDbContextFactory<GraphDbContext>
{
    public GraphDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GraphDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5455;Database=praxis;Username=praxis;Password=praxis_dev_only",
                npgsql => npgsql.MigrationsHistoryTable("__migrations", GraphDbContext.Schema))
            .Options;

        return new GraphDbContext(options);
    }
}
