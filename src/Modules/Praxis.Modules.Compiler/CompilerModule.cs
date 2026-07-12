using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Compiler.Persistence;

namespace Praxis.Modules.Compiler;

public static class CompilerModule
{
    public static IServiceCollection AddCompiler(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<CompilerDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", CompilerDbContext.Schema)));

        services.AddScoped<PlanService>();

        // The pass that catches the learner who fell behind while nothing happened.
        services.AddSingleton<NightlyRecompile>();
        services.AddHostedService(sp => sp.GetRequiredService<NightlyRecompile>());

        return services;
    }
}
