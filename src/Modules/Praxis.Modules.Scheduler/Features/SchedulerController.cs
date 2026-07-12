using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Modules.Scheduler.Features.Today;
using Praxis.Shared;

namespace Praxis.Modules.Scheduler.Features;

/// **One endpoint. One card. One action.**
///
/// There is deliberately no `GET /queue`, no `GET /recommendations`, and no `?limit=10`. Every one
/// of those would be a list, and a list is a choice, and a choice handed to a learner at the moment
/// they sit down is the moment most of them get up again.
[ApiController]
[Route("api/scheduler")]
[Authorize]
public sealed class SchedulerController : ControllerBase
{
    /// *"I have fifteen minutes. What do I do?"*
    [HttpGet("today")]
    public async Task<IActionResult> Today(
        [FromQuery] GetToday.Query query,
        [FromServices] GetToday.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(query, ct)).ToApiResponse();
}
