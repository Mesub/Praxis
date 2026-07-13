using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Praxis.Modules.Ledger.Domain;
using Praxis.Shared;

namespace Praxis.Modules.Ledger.Features.Verification;

/// **The public, free-forever verification surface — the cold-start weapon at the HTTP boundary**
/// (R-7).
///
/// Every route here is `[AllowAnonymous]`, and that is a permanent commitment, not a Phase-0
/// convenience: a Verified Competency Record is worthless if checking it needs an account. An employer
/// who has never heard of PRAXIS pastes a record here and gets a yes/no plus the reasons — no login, no
/// call to a learner's private data, no fee. That is what makes the record accepted, and acceptance is
/// the whole moat.
///
/// ## The one refusal that matters here
///
/// **Verification uses the server's published key, never a key from the request.** A forger can put
/// their own public key inside the record and sign with the matching private key — an internally
/// consistent lie. This endpoint ignores the record's self-asserted `PublicKey` entirely and checks
/// the signature against the key **the server holds and publishes**. That is the difference between
/// "this record vouches for itself" (worthless) and "PRAXIS vouches for this record" (the product).
[ApiController]
[Route("api/ledger")]
[AllowAnonymous]
public sealed class VerificationController : ControllerBase
{
    /// **The trust root, handed out freely.** A verifier fetches this once and can then check any
    /// PRAXIS record offline, forever — they never need to come back.
    [HttpGet("public-key")]
    public IActionResult PublicKey([FromServices] ILedgerKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        return Result<PublicKeyResponse>.Success(new PublicKeyResponse(
            Algorithm: "Ed25519",
            PublicKeyBase64: Convert.ToBase64String(keys.PublicKey))).ToApiResponse();
    }

    /// **Check a record.** Returns whether it is a genuine, unaltered PRAXIS record, how many
    /// competencies verified, and — when it fails — a plain-language reason a non-cryptographer can
    /// act on.
    [HttpPost("verify")]
    public IActionResult Verify(
        [FromBody] CompetencyRecord record,
        [FromServices] ILedgerKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (record is null)
        {
            return Result<VerificationResponse>.Invalid("There was no record to verify.").ToApiResponse();
        }

        // **Against the server's published key — not `record.PublicKey`.** This line is the whole
        // guarantee: the record does not get to nominate the authority that vouches for it.
        var result = CompetencyRecords.Verify(record, keys.PublicKey);

        return Result<VerificationResponse>.Success(new VerificationResponse(
            Valid: result.IsValid,
            VerifiedCompetencies: result.VerifiedCompetencies,
            Reason: result.Reason)).ToApiResponse();
    }

    /// **The same check, rendered for a human.** An employer can eyeball the competencies and the big
    /// green (or red) verdict without reading JSON. Self-contained HTML — no external assets, nothing
    /// to fetch, so it works pasted into any browser or archived offline.
    [HttpPost("verify/page")]
    [Produces("text/html")]
    public IActionResult VerifyPage(
        [FromBody] CompetencyRecord record,
        [FromServices] ILedgerKeys keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (record is null)
        {
            return Content(VerificationPage.NoRecord(), "text/html");
        }

        var result = CompetencyRecords.Verify(record, keys.PublicKey);

        return Content(VerificationPage.Render(record, result), "text/html");
    }
}

public sealed record PublicKeyResponse(string Algorithm, string PublicKeyBase64);

public sealed record VerificationResponse(bool Valid, int VerifiedCompetencies, string? Reason);

/// **The human-readable page.** Deliberately plain and self-contained: the point is that a stranger
/// trusts the *verdict*, and the verdict comes from the maths, not from how impressive the page looks.
internal static class VerificationPage
{
    public static string Render(CompetencyRecord record, RecordVerification result)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(result);

        var verdict = result.IsValid
            ? $"""<p class="ok">✓ Verified — issued by PRAXIS, unaltered. {result.VerifiedCompetencies} competency(ies) proven.</p>"""
            : $"""<p class="bad">✗ Not verified. {Escape(result.Reason ?? "Unknown reason.")}</p>""";

        var rows = new StringBuilder();

        foreach (var c in record.Competencies)
        {
            // **Assurance shown honestly, next to the score** — a competency held on a model's say-so
            // reads as such, because the record refuses to launder it (C-6). Only the score/assurance
            // that survived verification are worth reading; if the record did not verify, the page says
            // so at the top and these are shown as *claimed*, not confirmed.
            rows.Append($"""
                <tr>
                  <td>{Escape(c.ConceptName)}</td>
                  <td>{Escape(c.Type.ToString())}</td>
                  <td>{c.Score:P0}</td>
                  <td>{c.Assurance:P0}<span class="how"> ({Escape(c.GradingMethod.ToString())})</span></td>
                </tr>
                """);
        }

        return $"""
            <main class="vcr">
              <h1>Verified Competency Record</h1>
              <p class="subject">{Escape(record.Subject)}</p>
              {verdict}
              <table>
                <thead><tr><th>Competency</th><th>Kind</th><th>Score</th><th>Assurance</th></tr></thead>
                <tbody>{rows}</tbody>
              </table>
              <p class="note">
                This page is a rendering of a signed record. Its verdict comes from checking a
                cryptographic signature against PRAXIS's published public key — not from anything on
                this page. Anyone can run the same check, offline, for free.
              </p>
            </main>
            """;
    }

    public static string NoRecord()
        => """<main class="vcr"><h1>Verified Competency Record</h1><p class="bad">No record was supplied.</p></main>""";

    /// **The record is untrusted input** — a `Subject` or `ConceptName` is whatever the presenter
    /// typed, so every field is HTML-escaped before it reaches the page. A verification page that could
    /// be XSS'd by the thing it is verifying would be its own worst advertisement.
    private static string Escape(string s)
        => s.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
