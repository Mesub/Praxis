using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Assessment.Projections;
using Praxis.Modules.Kernel.Projections;

namespace Praxis.Modules.Assessment;

public static class AssessmentModule
{
    public static IServiceCollection AddAssessment(
        this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<AssessmentDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", AssessmentDbContext.Schema)));

        // Both of these are *derived*. Neither is a place anybody can write a belief about a learner
        // by hand — the only way in is to append an event saying something happened.
        services.AddScoped<IProjection, AbilityProjection>();
        services.AddScoped<IProjection, MisconceptionProjection>();

        return services;
    }
}
