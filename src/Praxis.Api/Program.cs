using Praxis.Infrastructure;
using Praxis.Infrastructure.Web;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Engine assemblies get appended here as each one lands. Their slices — handlers and
// validators — are then discovered by convention rather than registered by hand.
builder.Services.AddPraxisInfrastructure(typeof(Program).Assembly);

builder.Services.AddControllers(options =>
{
    // Every action is validated before it runs. A handler cannot receive invalid input.
    options.Filters.Add<ValidationFilter>();
});

builder.Services.AddOpenApi();

var app = builder.Build();

// Expected failures came back as Results and were already mapped. Anything reaching here
// is a bug or an infrastructure fault.
app.UseExceptionHandler();

app.UseSerilogRequestLogging();

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
