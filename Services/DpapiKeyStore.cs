using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace StudentDesktop.Services;

// 0.3 spike (SDA/SEK plan): Windows implementation of ISecureKeyStore via DPAPI
// (System.Security.Cryptography.ProtectedData, built into .NET — no extra dependency).
// DPAPI itself only encrypts/decrypts a blob against a key derived from the current
// Windows user's credentials; it has no notion of named key-value storage, so this class
// pairs it with a plain file per key under LocalApplicationData (matching ThemeService's
// existing settings-file convention) — the FILE CONTENTS are what DPAPI protects, not the
// filename/location.
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyStore : ISecureKeyStore
{
    private static readonly string StoreDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudentDesktop", "keystore");

    public Task SetAsync(string key, byte[] value)
    {
        Directory.CreateDirectory(StoreDir);
        // CurrentUser scope (not LocalMachine): the whole point is that only the signed-in
        // Windows user who wrote it can read it back — a different user account on the
        // same machine must not be able to decrypt another student's local store key.
        var encrypted = ProtectedData.Protect(value, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), encrypted);
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<byte[]?>(null);
        }

        try
        {
            var encrypted = File.ReadAllBytes(path);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Task.FromResult<byte[]?>(decrypted);
        }
        catch (CryptographicException)
        {
            // Corrupt data, or protected by a different user's DPAPI key (e.g. the store
            // directory was copied to another machine/profile) — null, not a thrown
            // exception, matching GetAsync's documented "doesn't exist or can't be
            // decrypted, treat the same" contract. The caller (Work Item C) is the one
            // that decides whether that's fatal.
            return Task.FromResult<byte[]?>(null);
        }
    }

    public Task DeleteAsync(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private static string PathFor(string key) => Path.Combine(StoreDir, SanitizeKey(key) + ".bin");

    // Keys are app-chosen identifiers (e.g. "local-store-passphrase"), not untrusted
    // input, but sanitizing defensively is cheap and avoids ever depending on a key
    // string being a valid filename as-is.
    private static string SanitizeKey(string key) =>
        new(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
}
