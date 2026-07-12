using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Modules.Compiler.Features.Objectives;
using Praxis.Modules.Compiler.Features.Plans;
using Praxis.Shared;

namespace Praxis.Modules.Compiler.Features;

/// **Where PRAXIS is allowed to say no.**
///
/// There is no `POST /plans` here, and there never will be. A plan is not a thing you create — it
/// is the answer to a question, recomputed every time it is asked, because the person it describes
/// changed since you last asked. Storing one would create a second version of the truth, and every
/// bug in that class is the same bug: something reads a stale plan and acts on a learner who no
/// longer exists.
[ApiController]
[Route("api/compiler")]
[Authorize]
public sealed class CompilerController : ControllerBase
{
    /// What they are trying to become, and the minutes they will actually give it.
    [HttpPost("objectives")]
    public async Task<IActionResult> SetObjective(
        [FromBody] SetObjective.Command command,
        [FromServices] SetObjective.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(command, ct)).ToApiResponse();

    /// The compiled path — and whether PRAXIS believes in it.
    ///
    /// A `believable: false` still comes back **200 with the plan attached**. The refusal is not an
    /// error; it is the product's most considered opinion, and the learner is entitled to see both
    /// it and the plan it is about.
    [HttpGet("plan")]
    public async Task<IActionResult> GetPlan(
        [FromServices] GetPlan.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(new GetPlan.Query(), ct)).ToApiResponse();
}
