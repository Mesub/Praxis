using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Graph.Features.Concepts;

[ApiController]
[Route("api/graph/concepts")]
[Authorize]
public sealed class ConceptsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ListConcepts.Query query,
        [FromServices] ListConcepts.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(query, ct)).ToApiResponse();

    /// By slug, not id. A slug is what a pack, an item bank and a piece of Evidence all use to
    /// name the same idea — it is the concept's actual name.
    [HttpGet("{slug}")]
    public async Task<IActionResult> Get(
        string slug,
        [FromServices] GetConcept.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(new GetConcept.Query(slug), ct)).ToApiResponse();

    /// What this learner could start today, and how far away everything else is.
    ///
    /// `held` is supplied by the caller because the Graph does not know what anyone knows — the
    /// Twin does, and it derives it from evidence. Once the Twin lands (M3), the web console will
    /// stop passing this and the Scheduler will fill it in.
    [HttpPost("frontier")]
    public async Task<IActionResult> Frontier(
        [FromBody] GetFrontier.Query query,
        [FromServices] GetFrontier.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(query, ct)).ToApiResponse();
}
