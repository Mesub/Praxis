namespace Praxis.Modules.Ledger;

/// **The one key the whole verifiable record rests on.**
///
/// PRAXIS signs every learner's ledger with a private key it holds and never publishes, and the
/// matching **public** key is published so anyone can verify. This service is the seam between the
/// domain (which knows nothing about where keys live) and the composition root (which loads the real
/// key from a secret store).
///
/// The public key is the trust root of the entire product's most valuable asset. A verifier who has
/// it can check any PRAXIS record forever, offline, for free — which is exactly why the verification
/// endpoint uses **this** key and never a key carried inside the record it is checking.
public interface ILedgerKeys
{
    /// **Published.** Handed out freely — the more widely it is known, the more a PRAXIS record is
    /// worth.
    byte[] PublicKey { get; }

    /// **Sign, without ever handing out the key.** The private half signs the payload *inside* the
    /// implementation and only the signature comes back — the same shape as a hardware security
    /// module. A service that returned the raw private key would be a service that could be robbed of
    /// the one secret the whole product's value rests on; this one cannot, because the key never
    /// leaves. Issuance calls this; nothing else does.
    byte[] Sign(string payload);
}
