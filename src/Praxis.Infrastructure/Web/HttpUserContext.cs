using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Praxis.Shared;

namespace Praxis.Infrastructure.Web;

/// Reads the learner off the JWT once, at the edge, so that no slice ever reaches into
/// HttpContext. Real token issuance arrives with Identity in M1.5.
public sealed class HttpUserContext : IUserContext
{
    /// The claim that carries the learner id. Kept here so the Identity module and the
    /// reader of the token cannot disagree about the name.
    public const string LearnerIdClaim = "praxis:learner_id";

    private readonly IHttpContextAccessor _accessor;

    public HttpUserContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? LearnerId
    {
        get
        {
            var value = _accessor.HttpContext?.User.FindFirstValue(LearnerIdClaim)
                        ?? _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => LearnerId is not null;

    public Guid RequireLearnerId()
        => LearnerId ?? throw new InvalidOperationException(
            "No learner on this request. A slice behind [Authorize] reached an anonymous "
            + "context, which means the auth pipeline is misconfigured — not that the caller "
            + "needs a friendlier error.");
}
