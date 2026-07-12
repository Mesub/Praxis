using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Shared;

namespace Praxis.Modules.Memory.Features.Reps;

/// **The front door of the Memory engine.**
///
/// Two routes. Read the ones that are missing.
///
/// There is no `GET /reps/{id}/answer` and no *"just show me"* — **a review queue is exactly where
/// that button would feel most reasonable, which is exactly why it does not exist.** The answer lives
/// behind the oracle on the far side of the AI-plane wire, and this process has never held one.
///
/// There is no `POST /reps/{id}/skip`, no `/snooze`, and no `/bury`. Not because burying a card is
/// wicked, but because every one of those endpoints exists in other products **to make a number go
/// down** — the number being the count of things you are behind on. PRAXIS does not show you that
/// number (M8.2), so there is nothing to bury.
///
/// And there is no `POST /reps/{id}/mark-as-known`. Mastery moves on evidence, and a button is not
/// evidence.
[ApiController]
[Route("api/memory/reps")]
[Authorize]
public sealed class RepsController : ControllerBase
{
    /// **What is worth reviewing today** — ordered by what a rep is *worth*, not by what you owe.
    ///
    /// An empty queue is a **200 with `nothingWorthDoing: true`**, not an error and not an
    /// opportunity to invent something. PRAXIS optimises for the learner leaving (I-10).
    [HttpGet]
    public async Task<IActionResult> Queue(
        [FromServices] GetReps.Handler handler,
        CancellationToken ct)
        => (await handler.HandleAsync(new GetReps.Query(), ct)).ToApiResponse();

    /// **They answered.** It goes on the log exactly like any other evidence, and it **cannot move
    /// mastery** (C-4).
    [HttpPost("{conceptId:guid}")]
    public async Task<IActionResult> Answer(
        Guid conceptId,
        [FromBody] RepRequest request,
        [FromServices] AnswerRep.Handler handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (await handler.HandleAsync(
            new AnswerRep.Command(
                conceptId,
                request.ItemId,
                request.Claim,
                request.StatedConfidence,
                request.Seconds),
            ct))
            .ToApiResponse();
    }
}

// The rules live on the type the wire actually produces, not on the Command the controller builds
// inside itself — because `ValidationFilter` validates what the action *binds*, and a validator that
// never runs is worse than no validator (M6.5b).

public sealed record RepRequest(
    Guid ItemId,
    string Claim,

    /// **Stated before they find out.** A rep is the sharpest calibration signal in the product: the
    /// learner has a *strong* opinion about whether they still know this, and it is very often wrong.
    double StatedConfidence,

    int Seconds);

public sealed class RepRequestValidator : AbstractValidator<RepRequest>
{
    public RepRequestValidator()
    {
        RuleFor(r => r.ItemId).NotEmpty();

        RuleFor(r => r.Claim)
            .NotEmpty()
            .WithMessage("An empty answer is not an answer, and PRAXIS will not mark one.")
            .MaximumLength(20_000);

        RuleFor(r => r.StatedConfidence)
            .InclusiveBetween(0.0, 1.0)
            .WithMessage(
                "Confidence is 0 to 1, and PRAXIS asks *before* telling you, because 'I was sure and "
                + "it had gone' is the single most useful sentence a review can produce — and it is "
                + "unsayable if you are asked afterwards.");

        RuleFor(r => r.Seconds).GreaterThanOrEqualTo(0);
    }
}
