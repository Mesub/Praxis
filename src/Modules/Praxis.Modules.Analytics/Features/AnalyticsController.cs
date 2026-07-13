using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Analytics.Features;

/// **The scoreboard PRAXIS keeps on itself.** One number, and it is allowed to be bad.
[ApiController]
[Route("api/analytics")]
[Authorize]
public sealed class AnalyticsController : ControllerBase
{
    /// **Your Mastery-per-Minute** — what your attention actually bought, reported honestly whether it
    /// is high, low, or nothing.
    [HttpGet("me")]
    public async Task<IActionResult> Me(
        [FromServices] GetAnalytics.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(new GetAnalytics.Query(), ct)).ToApiResponse();
}
