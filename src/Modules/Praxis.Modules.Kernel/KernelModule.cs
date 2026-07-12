using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Kernel.Projections;

namespace Praxis.Modules.Kernel;

public static class KernelModule
{
    public static IServiceCollection AddKernel(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<KernelDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__migrations", KernelDbContext.Schema)));

        services.AddScoped<IEventStore, EventStore>();
        services.AddScoped<ProjectionRunner>();

        return services;
    }

    /// Every engine that projects the log registers itself here. The runner picks up whatever
    /// is registered — so a projection that forgets to register is simply never fed, which is
    /// why M1.6 checks the rebuild against the projections the app actually has.
    public static IServiceCollection AddProjection<TProjection>(this IServiceCollection services)
        where TProjection : class, IProjection
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TProjection>();
        services.AddScoped<IProjection>(sp => sp.GetRequiredService<TProjection>());

        return services;
    }
}
