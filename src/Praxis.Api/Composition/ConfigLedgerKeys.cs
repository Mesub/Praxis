using Microsoft.Extensions.Configuration;
using Praxis.Modules.Ledger;
using Praxis.Modules.Ledger.Domain;

namespace Praxis.Api.Composition;

/// **Where the Ledger's signing key actually comes from — the composition root, not the domain.**
///
/// The private seed is a **secret**: it lives in configuration that is gitignored (the same place as
/// the API keys — see CLAUDE.md), and in production in a secret store. The domain knows nothing about
/// any of this; it is handed a public key to verify against, and that is all.
///
/// **The private seed is never exposed past this class.** It is used once, here, to derive the public
/// key, and then only the public half leaves — because a service that hands out the private key is a
/// service that has published the thing that must never be published.
///
/// ## The dev fallback, and why it is loud
///
/// With no configured seed, a fresh one is generated per process. That is fine for a test run or a
/// local spin-up, and **wrong for anything that must outlive a restart** — records signed by a
/// per-process key stop verifying the moment the process recycles. So a real deployment must configure
/// `Ledger:PrivateKeySeed`, and the absence of it is a thing an operator should notice, not a silent
/// default that quietly invalidates every record issued before the last deploy.
public sealed class ConfigLedgerKeys : ILedgerKeys
{
    public ConfigLedgerKeys(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration["Ledger:PrivateKeySeed"];

        var privateSeed = string.IsNullOrWhiteSpace(configured)
            ? LedgerSigning.GenerateKeyPair().PrivateKeySeed   // dev-only, per-process
            : Convert.FromBase64String(configured);

        // Derive the public half and keep only that. The seed is not stored on this object.
        PublicKey = LedgerSigning.PublicKeyFor(privateSeed);
    }

    public byte[] PublicKey { get; }
}
