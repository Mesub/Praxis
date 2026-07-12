// The PRAXIS host. The real wiring — DI, handler scanning, validation, the global exception
// handler, logging — lands in M0.4. This is the scaffold it hangs from.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed so integration tests can drive the host through WebApplicationFactory.
public partial class Program;
