using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Identity.Features.Auth;

[ApiController]
[Route("api/identity/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] RegisterLearner.Command command,
        [FromServices] RegisterLearner.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(command, ct)).ToApiResponse();

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginLearner.Command command,
        [FromServices] LoginLearner.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(command, ct)).ToApiResponse();

    /// Who the token says you are. The one endpoint that proves the pipeline reads the learner
    /// off the JWT rather than trusting anything the caller sent.
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me([FromServices] IUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Result<MeResponse>.Success(new MeResponse(user.RequireLearnerId())).ToApiResponse();
    }
}

public sealed record MeResponse(Guid LearnerId);
