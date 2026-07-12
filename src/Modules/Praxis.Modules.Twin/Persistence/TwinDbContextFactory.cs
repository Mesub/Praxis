using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Praxis.Modules.Twin.Persistence;

/// Used only by `dotnet ef` at design time. The running app builds its context from DI.
public sealed class TwinDbContextFactory : IDesignTimeDbContextFactory<TwinDbContext>
{
    public TwinDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TwinDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5455;Database=praxis;Username=praxis;Password=praxis_dev_only",
                npgsql => npgsql.MigrationsHistoryTable("__migrations", TwinDbContext.Schema))
            .Options;

        return new TwinDbContext(options);
    }
}
