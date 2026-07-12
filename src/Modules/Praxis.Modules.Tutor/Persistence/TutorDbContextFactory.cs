using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Praxis.Modules.Tutor.Persistence;

/// Used only by `dotnet ef` at design time. The running app builds its context from DI.
public sealed class TutorDbContextFactory : IDesignTimeDbContextFactory<TutorDbContext>
{
    public TutorDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TutorDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5455;Database=praxis;Username=praxis;Password=praxis_dev_only",
                npgsql => npgsql.MigrationsHistoryTable("__migrations", TutorDbContext.Schema))
            .Options;

        return new TutorDbContext(options);
    }
}
