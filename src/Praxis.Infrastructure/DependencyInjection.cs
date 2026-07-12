using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Praxis.Infrastructure.Web;
using Praxis.Shared;

namespace Praxis.Infrastructure;

public static class DependencyInjection
{
    /// Every engine's slices get discovered and registered here. A new use case is a new file —
    /// never a new line of DI wiring, because a line of wiring is a line someone forgets.
    ///
    /// The convention Scrutor keys off: a slice's handler is a class named `Handler`, nested in a
    /// static class named for the use case (`CreateConcept.Handler`). See ARCHITECTURE_STANDARD §3.
    public static IServiceCollection AddPraxisInfrastructure(
        this IServiceCollection services,
        params Assembly[] engineAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(engineAssemblies);

        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, HttpUserContext>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        services.AddScoped<ValidationFilter>();

        if (engineAssemblies.Length == 0)
        {
            return services;
        }

        services.Scan(scan => scan
            .FromAssemblies(engineAssemblies)
            .AddClasses(c => c.Where(t => t.Name == "Handler" && t is { IsAbstract: false, IsNested: true }))
            .AsSelf()
            .WithScopedLifetime());

        services.AddValidatorsFromAssemblies(engineAssemblies, ServiceLifetime.Scoped);

        return services;
    }
}
