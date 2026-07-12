using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Praxis.Modules.Ledger.Domain;

/// **The two layers the hash chain cannot provide on its own: authenticity, and un-rewritability.**
///
/// The chain (M10.2) proves *internal consistency* — nobody edited history without the break showing.
/// It does not stop an attacker who rewrites a record and recomputes every hash after it: the result
/// is a chain that verifies perfectly and is entirely false. Two things close that gap, and neither is
/// a blockchain.
///
/// ## Signing (Ed25519) — the head is unforgeable
///
/// PRAXIS signs the head of the chain with a private key it holds and never publishes. Anyone with the
/// public key can check the signature; **nobody without the private key can produce one.** So a forged
/// chain, however internally consistent, cannot carry a valid PRAXIS signature — the attacker can
/// rewrite the records, but they cannot sign the lie. A Verified Competency Record an employer checks
/// is trustworthy because the signature is, not because they trust the person who handed it over.
///
/// Ed25519 specifically: small keys, small signatures, fast verification, and **no parameter choices
/// to get wrong** — unlike ECDSA, whose per-signature nonce has ended more than one company when reused
/// or mis-generated. A boring, correct default.
///
/// ## Published Merkle roots — history cannot be rewritten in secret (see `LedgerMerkle`)
///
/// A signature stops forgery going forward, but PRAXIS holds its own key — so what stops PRAXIS, or
/// someone who steals the key, rewriting the past? The records are folded into a Merkle tree and its
/// **root is published** periodically to an append-only public place. To rewrite one past record you
/// would have to change the root, and the old root is already out in the world. **You cannot
/// un-publish.** This is the property people reach for a blockchain to get, obtained with a hash
/// function and a public noticeboard.
///
/// ## Why not a blockchain (the answer is no, permanently — doc 25 §7)
///
/// A signed hash chain + published Merkle roots + open verification gives every property that matters:
/// tamper-evidence, authenticity, un-rewritability, third-party verifiability. **A blockchain adds
/// cost, latency, and no trust we do not already have** — the trust still bottoms out in *"is this the
/// real PRAXIS public key"*, which a chain of blocks does not answer. We will be asked this every
/// month; it is written here so the answer does not have to be re-derived each time.
public static class LedgerSigning
{
    /// Sign the head hash of a chain. Because each entry's hash commits to all of history, **signing
    /// the head signs the whole ledger** in 64 bytes.
    public static byte[] Sign(string headHash, byte[] privateKeySeed)
    {
        ArgumentException.ThrowIfNullOrEmpty(headHash);
        ArgumentNullException.ThrowIfNull(privateKeySeed);

        var privateKey = new Ed25519PrivateKeyParameters(privateKeySeed, 0);

        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, privateKey);

        var message = Encoding.UTF8.GetBytes(headHash);
        signer.BlockUpdate(message, 0, message.Length);

        return signer.GenerateSignature();
    }

    /// **Anyone with the public key can check the signature; nobody without the private key can forge
    /// one.** This is the whole basis of a verifiable record — the checker trusts the maths, not us.
    public static bool Verify(string headHash, byte[] signature, byte[] publicKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(headHash);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKey);

        try
        {
            var key = new Ed25519PublicKeyParameters(publicKey, 0);

            var verifier = new Ed25519Signer();
            verifier.Init(forSigning: false, key);

            var message = Encoding.UTF8.GetBytes(headHash);
            verifier.BlockUpdate(message, 0, message.Length);

            return verifier.VerifySignature(signature);
        }
        catch (Exception e) when (e is ArgumentException or CryptoException)
        {
            // A malformed key or signature is **not** a valid signature — and a verifier that throws
            // its way out of a check is a verifier an attacker controls. Bad input verifies as false,
            // never as true, and never as an exception the caller might mishandle into "trusted".
            return false;
        }
    }

    /// Generate a fresh signing key pair. The **private** half is the 32-byte seed PRAXIS holds and
    /// never publishes; the **public** half is published so anyone can verify.
    public static (byte[] PrivateKeySeed, byte[] PublicKey) GenerateKeyPair()
    {
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var publicKey = privateKey.GeneratePublicKey();

        return (privateKey.GetEncoded(), publicKey.GetEncoded());
    }
}
