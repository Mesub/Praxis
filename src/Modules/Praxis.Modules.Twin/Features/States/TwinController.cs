using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Twin.Features.States;

/// **A learner's Twin is theirs.**
///
/// Every endpoint here reads the learner off the token and serves only them. There is no
/// `?learnerId=` parameter, and there never will be — the most intimate model of a person ever
/// built does not get an admin override (CANON §5.9).
[ApiController]
[Route("api/twin")]
[Authorize]
public sealed class TwinController : ControllerBase
{
    /// Everything, summarised — including how much of what they know is currently rotting.
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] GetTwin.Query query,
        [FromServices] GetTwin.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(query, ct)).ToApiResponse();

    /// One concept: what we believe, how fast it is going, what they are wrong about, and how much
    /// of it rests on work a model could not have faked.
    [HttpGet("concepts/{conceptId:guid}")]
    public async Task<IActionResult> GetConcept(
        Guid conceptId,
        [FromServices] GetConceptState.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(new GetConceptState.Query(conceptId), ct)).ToApiResponse();
}
