using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Features.TeachBacks;

/// **The Feynman test, as a front door.**
///
/// Read the route, then read the one that is missing. There is no `GET /teach-backs/{id}/rubric` and
/// no `?includeRubric=true`: the rubric is answer-adjacent and lives on the far side of the AI-plane
/// wire (M11.7a), and nothing in this process can return one because nothing in this process holds
/// one. What comes back is the verdict on *your* explanation, and the honest ceiling on what it can
/// do — never the standard it was measured against.
[ApiController]
[Route("api/assessment/teach-backs")]
[Authorize]
public sealed class TeachBacksController : ControllerBase
{
    /// **Explain it in your own words, and a rubric — not the tutor — grades how well.**
    ///
    /// The `Command` is bound directly, so its validator is the one that actually runs (M6.5b): a
    /// validator on a type the wire does not produce never fires, and everybody downstream then
    /// believes the input was checked.
    [HttpPost]
    public async Task<IActionResult> Record(
        [FromBody] RecordTeachBack.Command command,
        [FromServices] RecordTeachBack.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(command, ct)).ToApiResponse();
}
