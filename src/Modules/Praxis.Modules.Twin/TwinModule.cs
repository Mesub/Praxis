using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Twin.Persistence;

namespace Praxis.Modules.Twin;

public static class TwinModule
{
    public static IServiceCollection AddTwin(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<TwinDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", TwinDbContext.Schema)));

        return services;
    }
}
