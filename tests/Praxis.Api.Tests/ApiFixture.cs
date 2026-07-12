using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Praxis.Infrastructure;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Compiler.Persistence;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Identity.Persistence;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Tutor.Persistence;
using Praxis.Modules.Twin.Persistence;
using Testcontainers.PostgreSql;

namespace Praxis.Api.Tests;

/// The real host, on a real database, with the probe slice bolted on as an extra application part.
///
/// One container for the whole assembly: spinning up Postgres per test class would triple the
/// suite's runtime for no extra confidence.
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("praxis_test")
            .WithUsername("praxis")
            .WithPassword("praxis_test_only")
            .Build();

    public PraxisApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new PraxisApiFactory(_container.GetConnectionString());

        // Migrate through the host's own DI, so the tests run against the schema the app builds.
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<KernelDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<GraphDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TwinDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<CompilerDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TutorDbContext>().Database.MigrateAsync();

        // Assessment's projections are registered with the runner, and the runner catches *every*
        // projection up on *every* append. A missing `assessment` schema therefore does not fail an
        // assessment test -- it fails the next tutor session anybody starts.
        await scope.ServiceProvider.GetRequiredService<AssessmentDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

public sealed class PraxisApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public PraxisApiFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("ConnectionStrings:Praxis", _connectionString);
        builder.UseSetting("Jwt:Issuer", "praxis-test");
        builder.UseSetting("Jwt:Audience", "praxis-test");
        builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-at-least-32-bytes-long");

        builder.ConfigureTestServices(services =>
        {
            // Register the probe slice the way a real engine's assembly is registered — by
            // convention, not by hand.
            services.AddPraxisInfrastructure(typeof(PraxisApiFactory).Assembly);

            services.AddControllers()
                    .AddApplicationPart(typeof(PraxisApiFactory).Assembly);
        });
    }
}

[CollectionDefinition(SharedApi.Name)]
public sealed class SharedApi : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
