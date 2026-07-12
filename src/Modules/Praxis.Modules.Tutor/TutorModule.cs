using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Tutor.Persistence;

namespace Praxis.Modules.Tutor;

public static class TutorModule
{
    public static IServiceCollection AddTutor(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<TutorDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", TutorDbContext.Schema)));

        return services;
    }
}
