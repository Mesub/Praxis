using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Praxis.Modules.Kernel.Persistence;

/// Used only by `dotnet ef` at design time. The running app builds its context from DI.
public sealed class KernelDbContextFactory : IDesignTimeDbContextFactory<KernelDbContext>
{
    public KernelDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5433;Database=praxis;Username=praxis;Password=praxis_dev_only",
                npgsql => npgsql.MigrationsHistoryTable("__migrations", KernelDbContext.Schema))
            .Options;

        return new KernelDbContext(options);
    }
}
