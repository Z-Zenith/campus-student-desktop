using System.Runtime.Versioning;
using System.Text;
using StudentDesktop.Services;

namespace StudentDesktop.Tests;

// 0.3 spike (SDA/SEK plan): real DPAPI round-trip tests — this dev environment is
// Windows, so unlike KeychainKeyStore/LibsecretKeyStore (unverified, no macOS/Linux
// available here), this one can and should be genuinely exercised, not just compiled.
[SupportedOSPlatform("windows")]
public class DpapiKeyStoreTests
{
    private static string UniqueKey([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        $"test-{name}-{Guid.NewGuid():N}";

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsTheExactBytes()
    {
        var store = new DpapiKeyStore();
        var key = UniqueKey();
        var value = Encoding.UTF8.GetBytes("a secret passphrase, 🔒 and some bytes");

        await store.SetAsync(key, value);
        var retrieved = await store.GetAsync(key);

        Assert.Equal(value, retrieved);

        await store.DeleteAsync(key);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForAKeyThatWasNeverSet()
    {
        var store = new DpapiKeyStore();

        var result = await store.GetAsync(UniqueKey());

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheKey_SoASubsequentGetReturnsNull()
    {
        var store = new DpapiKeyStore();
        var key = UniqueKey();
        await store.SetAsync(key, [1, 2, 3]);

        await store.DeleteAsync(key);
        var result = await store.GetAsync(key);

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_OverwritesAnExistingValueForTheSameKey()
    {
        var store = new DpapiKeyStore();
        var key = UniqueKey();
        await store.SetAsync(key, [1, 2, 3]);

        await store.SetAsync(key, [4, 5, 6]);
        var result = await store.GetAsync(key);

        Assert.Equal(new byte[] { 4, 5, 6 }, result);

        await store.DeleteAsync(key);
    }

    // The whole point of this subsystem: the bytes are genuinely encrypted at rest, not
    // just written plainly to a file with an obscure name — assert none of the store's
    // on-disk files contain the plaintext value anywhere (checking every file, rather
    // than guessing the exact filename DpapiKeyStore's private sanitizer would produce,
    // keeps this test decoupled from that implementation detail).
    [Fact]
    public async Task SetAsync_TheOnDiskFiles_DoNotContainThePlaintextValue()
    {
        var store = new DpapiKeyStore();
        var key = UniqueKey();
        var plaintextMarker = "TOTALLY-UNENCRYPTED-MARKER-VALUE-12345";
        var value = Encoding.UTF8.GetBytes(plaintextMarker);

        await store.SetAsync(key, value);

        var storeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudentDesktop", "keystore");
        foreach (var file in Directory.GetFiles(storeDir, "*.bin"))
        {
            var rawText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
            Assert.DoesNotContain(plaintextMarker, rawText);
        }

        await store.DeleteAsync(key);
    }
}

public class SecureKeyStoreFactoryTests
{
    [Fact]
    public void Create_ReturnsADpapiKeyStore_OnWindows()
    {
        // This dev environment/CI-equivalent is Windows — see 0.3's own scope note on
        // shipping Windows-verified first.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = SecureKeyStoreFactory.Create();

        Assert.IsType<DpapiKeyStore>(store);
    }
}
