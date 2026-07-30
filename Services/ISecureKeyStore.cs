using System.Threading.Tasks;

namespace StudentDesktop.Services;

// 0.3 spike (SDA/SEK plan) — cross-platform secure local key storage. No existing
// precedent in this codebase, and no single NuGet package covers all three platforms
// cleanly, so this is hand-rolled per platform — same approach Git Credential Manager
// (a real, widely-shipped cross-platform .NET tool solving exactly this problem) uses,
// rather than trusting an unverified "cross-platform secure storage" package name.
//
// This exists to back Work Item C's encrypted local scratch storage (SQLCipher passphrase
// storage) — not yet wired up to that; this is the storage primitive itself.
public interface ISecureKeyStore
{
    Task SetAsync(string key, byte[] value);

    /// Returns null if the key doesn't exist, or if stored data exists but can't be
    /// decrypted (e.g. moved to a different user profile) — callers must treat both the
    /// same way (see SecureKeyStoreFactory's doc comment on failing loudly rather than
    /// silently falling back to an unencrypted store).
    Task<byte[]?> GetAsync(string key);

    Task DeleteAsync(string key);
}
