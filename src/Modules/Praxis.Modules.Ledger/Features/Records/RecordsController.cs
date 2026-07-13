using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Features.Records;

/// **Your own record.** Distinct from the anonymous `/verify` routes: those let *anyone* check a
/// record they were handed, needing no secret; this issues *your* record, needs a learner, and is the
/// only place PRAXIS's private key is ever used.
[ApiController]
[Route("api/ledger")]
[Authorize]
public sealed class RecordsController : ControllerBase
{
    /// **Issue and download the record of everything you currently hold**, signed by PRAXIS.
    [HttpGet("me")]
    public async Task<IActionResult> Me(
        [FromServices] IssueMyRecord.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(new IssueMyRecord.Query(), ct)).ToApiResponse();
}
