using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Praxis.Modules.Assessment.Persistence;

/// Used only by `dotnet ef` at design time. The running app builds its context from DI.
public sealed class AssessmentDbContextFactory : IDesignTimeDbContextFactory<AssessmentDbContext>
{
    public AssessmentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5455;Database=praxis;Username=praxis;Password=praxis_dev_only",
                npgsql => npgsql.MigrationsHistoryTable("__migrations", AssessmentDbContext.Schema))
            .Options;

        return new AssessmentDbContext(options);
    }
}
