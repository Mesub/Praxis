using Microsoft.EntityFrameworkCore;
using Praxis.Api.Composition;
using Praxis.Infrastructure;
using Praxis.Infrastructure.Web;
using Praxis.Modules.Compiler;
using Praxis.Modules.Compiler.Persistence;
using Praxis.Modules.Graph;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Identity;
using Praxis.Modules.Identity.Auth;
using Praxis.Modules.Identity.Persistence;
using Praxis.Modules.Kernel;
using Praxis.Modules.Twin;
using Praxis.Modules.Twin.Persistence;
using Praxis.Modules.Twin.Projections;
using Praxis.Modules.Kernel.Persistence;
using Praxis.Modules.Kernel.Projections;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

var connectionString = builder.Configuration.GetConnectionString("Praxis")
    ?? "Host=localhost;Port=5455;Database=praxis;Username=praxis;Password=praxis_dev_only";

// Engine assemblies get appended here as each one lands. Their slices — handlers and validators —
// are discovered by convention rather than registered by hand.
builder.Services.AddPraxisInfrastructure(
    typeof(Program).Assembly,
    typeof(IdentityModule).Assembly,
    typeof(GraphModule).Assembly,
    typeof(TwinModule).Assembly);

builder.Services.AddKernel(connectionString);
builder.Services.AddGraph(connectionString);
builder.Services.AddTwin(connectionString);
builder.Services.AddCompiler(connectionString);

// The seam between the Graph and the Twin: neither module references the other, so the contract
// crosses as a value and the adapter lives here, where the application is assembled.
builder.Services.AddScoped<IMasteryContracts, GraphMasteryContracts>();
builder.Services.AddProjection<TwinProjection>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddIdentityModule(connectionString, jwt);

builder.Services.AddControllers(options =>
{
    // Every action is validated before it runs. A handler cannot receive invalid input.
    options.Filters.Add<ValidationFilter>();
});

builder.Services.AddOpenApi();

var app = builder.Build();

// ── Operational commands ────────────────────────────────────────────────────
//
// `dotnet run -- --migrate`             apply pending migrations, then exit
// `dotnet run -- --rebuild-projections` destroy every projection and recompute it from the log
//
// The rebuild is not a repair tool. It is the standing proof that the event log is the source of
// truth: everything PRAXIS believes about a learner must be reachable by replaying history alone.
// If it ever stops being runnable, some belief is being kept somewhere history cannot audit.

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();

    await scope.ServiceProvider.GetRequiredService<KernelDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<GraphDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<TwinDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CompilerDbContext>().Database.MigrateAsync();

    Log.Information("Migrations applied.");
    return;
}

if (args.Contains("--rebuild-projections", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var runner = scope.ServiceProvider.GetRequiredService<ProjectionRunner>();

    var result = await runner.RebuildAsync();

    Log.Information(
        "Rebuilt {Count} projection(s) from {Events} event(s).",
        result.ProjectionCount,
        result.EventsApplied);

    return;
}

// ── The request pipeline ────────────────────────────────────────────────────

// Expected failures came back as Results and were already mapped. Anything reaching here is a bug
// or an infrastructure fault.
app.UseExceptionHandler();

app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health");

await app.RunAsync();

// Exposed so integration tests can drive the real host through WebApplicationFactory.
public partial class Program;
