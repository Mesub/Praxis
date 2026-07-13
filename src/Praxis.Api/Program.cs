using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Praxis.Api.Composition;
using Praxis.Infrastructure;
using Praxis.Infrastructure.Ai;
using Praxis.Infrastructure.Web;
using Praxis.Modules.Assessment;
using Praxis.Modules.Assessment.Persistence;
using Praxis.Modules.Assessment.Projections;
using Praxis.Modules.Compiler;
using Praxis.Modules.Compiler.Persistence;
using Praxis.Modules.Graph;
using Praxis.Modules.Graph.Persistence;
using Praxis.Modules.Identity;
using Praxis.Modules.Identity.Auth;
using Praxis.Modules.Identity.Persistence;
using Praxis.Modules.Kernel;
using Praxis.Modules.Ledger;
using Praxis.Modules.Memory;
using Praxis.Modules.Scheduler;
using Praxis.Modules.Tutor;
using Praxis.Modules.Tutor.Persistence;
using Praxis.Modules.Tutor.Projections;
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
    typeof(TwinModule).Assembly,
    typeof(CompilerModule).Assembly,
    typeof(ISchedulerSnapshot).Assembly,
    typeof(TutorModule).Assembly,
    typeof(AssessmentModule).Assembly,
    typeof(IMemorySnapshot).Assembly,
    typeof(ILedgerKeys).Assembly,
    typeof(Praxis.Modules.Reality.Domain.LoopsBoard).Assembly);

builder.Services.AddKernel(connectionString);
builder.Services.AddGraph(connectionString);
builder.Services.AddTwin(connectionString);
builder.Services.AddCompiler(connectionString);
builder.Services.AddTutor(connectionString);
builder.Services.AddAssessment(connectionString);

// The seam between the Graph and the Twin: neither module references the other, so the contract
// crosses as a value and the adapter lives here, where the application is assembled.
builder.Services.AddScoped<IMasteryContracts, GraphMasteryContracts>();

// The Graph knows the subject; the Twin knows the learner; the Compiler knows neither. This is the
// only place the three meet.
builder.Services.AddScoped<ICompilerSnapshot, LiveCompilerSnapshot>();

// The Scheduler owns no tables and registers no module. It has nothing to persist: the card is a
// pure function of what the Twin believes and what the Compiler planned, and both of those are
// themselves derived from the event log. Storing today's card would be storing a *conclusion* — and
// a conclusion that outlives the evidence it was drawn from is the exact failure the kernel exists
// to prevent.
builder.Services.AddScoped<ISchedulerSnapshot, LiveSchedulerSnapshot>();

// E7 knows what forgetting does; the Twin knows what was proved. Neither knows the other exists, so
// the adapter lives here -- and it is what keeps the rep queue a pure function of plain values.
builder.Services.AddScoped<IMemorySnapshot, LiveMemorySnapshot>();

// The Ledger's public key is the trust root of a Verified Competency Record. The private half is a
// secret loaded here and never exposed; only the public key leaves this service.
builder.Services.AddSingleton<ILedgerKeys, ConfigLedgerKeys>();

builder.Services.AddProjection<TwinProjection>();

// The Answer Budget is a projection too, and that is what makes it honest: the only way to change
// what a learner has spent is to append an event saying they spent it. There is no admin endpoint,
// no support tool, and no migration that tops people up after a bad week.
builder.Services.AddProjection<AnswerBudgetProjection>();

// And the session. **Nobody leaves empty-handed**: a session in which the learner produced nothing
// cannot be completed, and the projection re-derives that rather than trusting the tutor's word.
builder.Services.AddProjection<SessionProjection>();

// What PRAXIS believes about a learner, and what it believes they wrongly believe. **Both derived,
// neither decided.** There is no endpoint that sets somebody's ability and none that declares a
// misconception fixed -- the only way in is to append an event saying something happened.
builder.Services.AddProjection<AbilityProjection>();
builder.Services.AddProjection<MisconceptionProjection>();

// The AI plane. **The only way anything in PRAXIS talks to a language model** — the model is chosen
// by the task, the call is attributed to a learner, and the answer is never in the tutor's context.
// A slice that new'd up an HttpClient and called Anthropic directly would bypass all three.
builder.Services.AddAiPlane(
    builder.Configuration["AiPlane:BaseUrl"] ?? "http://localhost:8000");

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddIdentityModule(connectionString, jwt);

builder.Services.AddControllers(options =>
{
    // Every action is validated before it runs. A handler cannot receive invalid input.
    options.Filters.Add<ValidationFilter>();
})
.AddJsonOptions(json =>
{
    // **Enums cross the wire as names, not as numbers.**
    //
    // `{"rung": 4}` is unreadable, unloggable, and -- worse -- silently *reorderable*: insert a rung
    // in the middle of the enum one afternoon and every client in the world starts asking for a
    // different one, with no error raised anywhere. `{"rung": "partialScaffold"}` breaks loudly
    // instead, which is the only kind of breaking worth having.
    json.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
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
    await scope.ServiceProvider.GetRequiredService<TutorDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AssessmentDbContext>().Database.MigrateAsync();

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
