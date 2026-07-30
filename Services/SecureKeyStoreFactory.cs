using System;

namespace StudentDesktop.Services;

// 0.3 spike (SDA/SEK plan): selects the right ISecureKeyStore for the current OS. Callers
// (Work Item C's local store) must fail loudly if the OS keystore is genuinely unavailable
// — e.g. a locked-down lab machine with no Secret Service daemon AND no writable fallback
// directory — rather than silently falling back to an unencrypted local store or a
// hardcoded key. This factory itself doesn't hide failures: it picks an implementation
// based on OS alone; whether that implementation can actually read/write at runtime is
// for the caller to check via a real GetAsync/SetAsync round trip, not assumed here.
public static class SecureKeyStoreFactory
{
    public static ISecureKeyStore Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiKeyStore();
        }
        if (OperatingSystem.IsMacOS())
        {
            return new KeychainKeyStore();
        }
        if (OperatingSystem.IsLinux())
        {
            return new LibsecretKeyStore();
        }
        throw new PlatformNotSupportedException(
            $"No secure key storage implementation is available for this platform ({Environment.OSVersion.Platform}).");
    }
}
