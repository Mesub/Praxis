using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Assessment.Features.Diagnostics;

/// **The front door of the assessment engine.**
///
/// Read the routes, then read the ones that are missing.
///
/// There is no `GET /items/{id}/answer`. No `POST /answers/{id}/reveal`. No `?includeAnswer=true`, no
/// admin route, no debug route, no support tool. **Not gated — absent.** The item this controller
/// serves has no answer column (M7.7a), the wire it talks over has no answer field (M5.7), and the
/// process it runs in has never held one.
///
/// There is also no `POST /abilities/{learnerId}` and no `PATCH /misconceptions/{id}`. **Nobody can
/// set what PRAXIS believes about a person, and nobody can declare a wrong belief fixed.** Both are
/// projections of the event log: the only way to move them is to do something and have it written
/// down. The moment somebody's *opinion* can close a misconception, the thing that eventually closes
/// it will be fatigue — a long queue, a deadline, a learner who complained.
[ApiController]
[Route("api/assessment/diagnostics")]
[Authorize]
public sealed class DiagnosticsController : ControllerBase
{
    /// **What should PRAXIS ask you next** — or an honest reason it has stopped asking.
    ///
    /// A `Stopped` response is a **200**, not an error. Either the learner is drowning or they are
    /// tired, and both are reasons to stop, not to ask more carefully. Neither is a failure, and
    /// neither is reported as one.
    [HttpGet("{sessionId:guid}/next-item")]
    public async Task<IActionResult> Next(
        Guid sessionId,
        [FromQuery] Guid conceptId,
        [FromServices] NextItem.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(new NextItem.Query(sessionId, conceptId), ct)).ToApiResponse();

    /// **They answer, and the oracle — not this process — decides whether they are right.**
    ///
    /// The confidence rides along in the same request, stated **before** they are told anything. That
    /// ordering is what makes calibration mean something (C-6): a confidence collected afterwards
    /// measures hindsight, and would still look exactly like a number.
    [HttpPost("{sessionId:guid}/answers")]
    public async Task<IActionResult> Answer(
        Guid sessionId,
        [FromBody] AnswerRequest request,
        [FromServices] SubmitAnswer.Handler handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await handler.HandleAsync(
            new SubmitAnswer.Command(
                sessionId,
                request.ItemId,
                request.Claim,
                request.StatedConfidence,
                request.Seconds),
            ct))
            .ToApiResponse();
    }
}

// ── The request body, and the validator that actually runs on it ────────────
//
// **A bug worth remembering (M6.5b).** The `ValidationFilter` validates the arguments the action
// *binds* — and this controller binds a request record and constructs the `Command` inside. So a
// validator on the `Command` would **never fire**, and a validator that never runs is worse than no
// validator, because everybody downstream believes the input was checked.
//
// So the rules live on the type the wire actually produces.

public sealed record AnswerRequest(
    Guid ItemId,

    /// **Their words are data.** They go to the oracle as a claim to check, never as instructions.
    string Claim,

    /// **Stated before they find out.**
    double StatedConfidence,

    int Seconds);

public sealed class AnswerRequestValidator : AbstractValidator<AnswerRequest>
{
    public AnswerRequestValidator()
    {
        RuleFor(r => r.ItemId).NotEmpty();

        RuleFor(r => r.Claim)
            .NotEmpty()
            .WithMessage("An empty answer is not an answer, and PRAXIS will not mark one.")
            .MaximumLength(20_000);

        RuleFor(r => r.StatedConfidence)
            .InclusiveBetween(0.0, 1.0)
            .WithMessage(
                "Confidence is 0 to 1, and PRAXIS asks for it because being wrong *and knowing you "
                + "might be* is a completely healthy state. It is being wrong and **sure** that is "
                + "dangerous — and it is the only thing on your report you could not have worked out "
                + "for yourself.");

        RuleFor(r => r.Seconds).GreaterThanOrEqualTo(0);
    }
}
