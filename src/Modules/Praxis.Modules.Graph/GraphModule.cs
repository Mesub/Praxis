using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Graph.Persistence;

namespace Praxis.Modules.Graph;

public static class GraphModule
{
    public static IServiceCollection AddGraph(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<GraphDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", GraphDbContext.Schema)));

        return services;
    }
}
