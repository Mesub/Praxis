using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Praxis.Modules.Compiler.Persistence;

/// Used only by `dotnet ef` at design time. The running app builds its context from DI.
public sealed class CompilerDbContextFactory : IDesignTimeDbContextFactory<CompilerDbContext>
{
    public CompilerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CompilerDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5455;Database=praxis;Username=praxis;Password=praxis_dev_only",
                npgsql => npgsql.MigrationsHistoryTable("__migrations", CompilerDbContext.Schema))
            .Options;

        return new CompilerDbContext(options);
    }
}
