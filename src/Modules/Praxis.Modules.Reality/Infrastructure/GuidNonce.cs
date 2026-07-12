using Praxis.Modules.Reality.Domain;

namespace Praxis.Modules.Reality.Infrastructure;

/// **The unguessable framing token, for real.**
///
/// The entire anti-forgery defence rests on the learner not being able to predict this. A `Guid` is
/// 122 bits of cryptographic randomness on every platform PRAXIS runs on — a learner cannot guess it,
/// and cannot brute-force it inside a ten-second wall clock. Prefixed with letters so it is always a
/// valid Python identifier fragment and can never accidentally be interpreted as anything else in the
/// harness.
public sealed class GuidNonce : INonceSource
{
    public string Next() => "px" + Guid.NewGuid().ToString("N");
}
