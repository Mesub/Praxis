using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Api.Tests.Probes;

/// A real vertical slice, existing only in the test assembly, so that the host's cross-cutting
/// machinery is exercised the way an engine will actually exercise it — a bound Command, a
/// registered Validator, a Handler returning Result<T>, and a controller that does nothing but
/// call .ToApiResponse().
///
/// If this slice behaves, every engine's slices will.
public static class ProbeSlice
{
    public record Command(string Name, int Minutes);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Minutes).GreaterThan(0).WithMessage("Minutes must be positive.");
        }
    }

    public sealed class Handler
    {
        private readonly IClock _clock;

        public Handler(IClock clock) => _clock = clock;

        public Task<Result<ProbeResponse>> HandleAsync(Command command, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (command.Name == "conflict")
            {
                return Task.FromResult(Result<ProbeResponse>.Conflict("Already exists."));
            }

            if (command.Name == "missing")
            {
                return Task.FromResult(Result<ProbeResponse>.NotFound("No such thing."));
            }

            if (command.Name == "boom")
            {
                throw new InvalidOperationException("A bug, not an expected failure.");
            }

            return Task.FromResult(Result<ProbeResponse>.Success(
                new ProbeResponse(command.Name, command.Minutes, _clock.UtcNow),
                "Probe accepted."));
        }
    }
}

public sealed record ProbeResponse(string Name, int Minutes, DateTime SeenAt);

[ApiController]
[Route("api/probe/slices")]
public sealed class ProbeController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] ProbeSlice.Command command,
        [FromServices] ProbeSlice.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(command, ct)).ToApiResponse();
}
