using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Tutor.Features.Sessions;

/// **The front door of the tutor.**
///
/// Read the routes and note the ones that are missing. There is no `GET /sessions/{id}/answer`. No
/// `POST /sessions/{id}/reveal`. No `?showAnswer=true`, no admin route, no debug route. The .NET
/// core has no answer to serve, because the answer lives behind the oracle on the far side of a wire
/// whose contract has no field for one (M5.7).
///
/// And there is no `POST /sessions/{id}/hints/buy`. The budget can be **earned** by producing
/// something (M6.2b) and it can never be **bought**, because a budget you can buy is a budget a
/// patient learner can use to grind the oracle until it gives up the answer.
[ApiController]
[Route("api/tutor/sessions")]
[Authorize]
public sealed class SessionsController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start(
        [FromBody] StartSession.Command command,
        [FromServices] StartSession.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(command, ct)).ToApiResponse();

    /// The session as it stands — **including the budget, which the learner is entitled to watch.**
    /// A scarcity you cannot see is not a scarcity, it is an ambush.
    [HttpGet("{sessionId:guid}")]
    public async Task<IActionResult> Get(
        Guid sessionId,
        [FromServices] GetSession.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(new GetSession.Query(sessionId), ct)).ToApiResponse();

    /// **A rung of the ladder.** Four gates fire here: the ladder, the budget, the AI plane's
    /// answer-free context, and the event log that will discount the credit afterwards.
    ///
    /// A **403** means the budget is gone — which is *not* an error and *not* a route to the answer.
    /// It is the point at which PRAXIS asks for a stuck note and recompiles the plan.
    [HttpPost("{sessionId:guid}/hints")]
    public async Task<IActionResult> Hint(
        Guid sessionId,
        [FromBody] HintRequest request,
        [FromServices] TakeHint.Handler handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await handler
            .HandleAsync(new TakeHint.Command(sessionId, request.Rung, request.LearnerSaid), ct))
            .ToApiResponse();
    }

    /// **They made something.** It earns budget back — including a genuine, reasoned *wrong* attempt,
    /// because PRAXIS pays you to fail properly (M6.2b).
    [HttpPost("{sessionId:guid}/productions")]
    public async Task<IActionResult> Produce(
        Guid sessionId,
        [FromBody] ProductionRequest request,
        [FromServices] RecordProduction.Handler handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await handler.HandleAsync(new RecordProduction.Command(sessionId, request.Kind), ct))
            .ToApiResponse();
    }

    /// *"Where exactly does your reasoning break?"* — the most valuable two sentences in the session,
    /// and the reason a blocked learner still leaves having made something.
    [HttpPost("{sessionId:guid}/stuck-note")]
    public async Task<IActionResult> StuckNote(
        Guid sessionId,
        [FromBody] StuckNoteRequest request,
        [FromServices] WriteStuckNote.Handler handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await handler.HandleAsync(new WriteStuckNote.Command(sessionId, request.Note), ct))
            .ToApiResponse();
    }

    /// Ending it. **A session in which the learner produced nothing cannot be completed** — they may
    /// make one thing, or they may say they are leaving. Both are true; "completed" would not be.
    [HttpPost("{sessionId:guid}/end")]
    public async Task<IActionResult> End(
        Guid sessionId,
        [FromBody] EndRequest request,
        [FromServices] EndSession.Handler handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await handler.HandleAsync(
            new EndSession.Command(sessionId, request.Abandoned, request.ActualMinutes), ct))
            .ToApiResponse();
    }
}

// ── The request bodies, and the validators that actually run on them ─────────
//
// **A subtle bug, and worth recording.** The `ValidationFilter` validates the arguments the action
// *binds* — and these controllers bind a request record and construct the `Command` inside. So the
// `Command` validators never fired: `"I dunno"` sailed straight through a twenty-character minimum
// on a stuck note, and the test that caught it was the only thing standing between that and
// production.
//
// **A validator that never runs is worse than no validator**, because everybody downstream believes
// the input was checked. So the rules live on the type the wire actually produces.

public sealed record HintRequest(
    Praxis.Modules.Kernel.Events.HintRung Rung,

    /// What the learner said in reply to the last turn. **LR-3**: without it, the ladder does not
    /// move.
    string? LearnerSaid = null);

public sealed class HintRequestValidator : AbstractValidator<HintRequest>
{
    public HintRequestValidator()
    {
        RuleFor(r => r.Rung)
            .IsInEnum()
            .WithMessage(
                "That is not a rung on the ladder. There are five, and the deepest gives you the "
                + "structure with the hole still in it — there is nothing below it.");

        RuleFor(r => r.LearnerSaid).MaximumLength(20_000);
    }
}

public sealed record ProductionRequest(Praxis.Modules.Kernel.Events.ProductionKind Kind);

public sealed class ProductionRequestValidator : AbstractValidator<ProductionRequest>
{
    public ProductionRequestValidator() => RuleFor(r => r.Kind).IsInEnum();
}

public sealed record StuckNoteRequest(string Note);

public sealed class StuckNoteRequestValidator : AbstractValidator<StuckNoteRequest>
{
    public StuckNoteRequestValidator()
        => RuleFor(r => r.Note)
            .NotEmpty()
            .MinimumLength(20)
            .WithMessage(
                "A stuck note says where your reasoning breaks. \"I don't know\" is not one — it "
                + "says *that* you are stuck, and PRAXIS already knows that. What it needs is "
                + "*where*: the last thing you were sure of, and the first thing you were not.")
            .MaximumLength(4_000);
}

public sealed record EndRequest(bool Abandoned, int ActualMinutes);

public sealed class EndRequestValidator : AbstractValidator<EndRequest>
{
    public EndRequestValidator() => RuleFor(r => r.ActualMinutes).GreaterThanOrEqualTo(0);
}
