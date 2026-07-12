using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Praxis.Infrastructure;

namespace Praxis.Api.Tests;

/// Drives the real host — the real filter pipeline, the real exception handler, the real DI —
/// with the probe slice bolted on as an extra application part.
public sealed class PraxisApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);

        builder.ConfigureTestServices(services =>
        {
            // Register the probe slice exactly the way a real engine's assembly would be
            // registered — by convention, not by hand.
            services.AddPraxisInfrastructure(typeof(PraxisApiFactory).Assembly);

            services.AddControllers()
                    .AddApplicationPart(typeof(PraxisApiFactory).Assembly);
        });
    }
}
