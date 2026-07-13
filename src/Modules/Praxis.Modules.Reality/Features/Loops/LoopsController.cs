using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Reality.Features.Loops;

/// **The board of everything you have predicted and cannot take back.**
///
/// Read the route, and note what is missing. There is no `DELETE /loops/{id}` and no
/// `?hide=resolved`: the whole epistemic value of a Reality Loop is that the commitment is
/// irrevocable, so there is nowhere here to withdraw one. An open loop stays on the board until
/// reality closes it — which it does through the resolve endpoint, not this one.
[ApiController]
[Route("api/reality/loops")]
[Authorize]
public sealed class LoopsController : ControllerBase
{
    /// Your open loops (soonest horizon first), what has resolved, and your Coverage — the
    /// anti-cherry-picking metric. Computed from the log on every request; nothing is stored.
    [HttpGet]
    public async Task<IActionResult> Board(
        [FromServices] GetLoopsBoard.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(new GetLoopsBoard.Query(), ct)).ToApiResponse();

    /// **Commit a prediction** — the claim and the confidence, frozen before the code runs (C-6). The
    /// `Command` is bound directly so its validator is the one that runs (M6.5b). It goes on the board
    /// and it cannot be taken back; resolution is a separate act.
    [HttpPost]
    public async Task<IActionResult> Commit(
        [FromBody] CommitPrediction.Command command,
        [FromServices] CommitPrediction.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(command, ct)).ToApiResponse();
}
