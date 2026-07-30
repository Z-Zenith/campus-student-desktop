using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.Tests;

// Work Item C (SDA/SEK plan): in-memory ISecureKeyStore so these tests don't touch the
// real OS keystore (DpapiKeyStoreTests already covers that round-trip for real) — this
// fake just needs to hand back whatever bytes were last set, matching the real contract's
// "null means never set" semantics.
public sealed class FakeSecureKeyStore : ISecureKeyStore
{
    private byte[]? _value;
    public bool ThrowOnAccess { get; set; }

    public Task SetAsync(string key, byte[] value)
    {
        if (ThrowOnAccess) throw new InvalidOperationException("Simulated keystore failure.");
        _value = value;
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string key)
    {
        if (ThrowOnAccess) throw new InvalidOperationException("Simulated keystore failure.");
        return Task.FromResult(_value);
    }

    public Task DeleteAsync(string key)
    {
        _value = null;
        return Task.CompletedTask;
    }
}

public class LocalStoreContextTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"local-store-tests-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task<LocalStoreContext> OpenAsync(FakeSecureKeyStore? keyStore = null) =>
        await LocalStoreContext.OpenAsync(keyStore ?? new FakeSecureKeyStore(), _dbPath);

    private static CodeProjectDto NewProject(Guid? id = null, string name = "proj") => new(
        id ?? Guid.NewGuid(), name,
        [new CodeFileDto("main.py", "python", "print('hello')")],
        "main.py", "main.py", null, DateTime.UtcNow, DateTime.UtcNow);

    [Fact]
    public async Task SaveCodeProjectAsync_ThenGetCodeProjectAsync_RoundTripsFilesAndMetadata()
    {
        await using var store = await OpenAsync();
        var project = NewProject(name: "My Project");

        await store.SaveCodeProjectAsync(project);
        var loaded = await store.GetCodeProjectAsync(project.Id);

        Assert.NotNull(loaded);
        Assert.Equal("My Project", loaded!.Name);
        Assert.Equal("main.py", loaded.EntryFilePath);
        var file = Assert.Single(loaded.Files);
        Assert.Equal("print('hello')", file.Content);
    }

    [Fact]
    public async Task GetCodeProjectAsync_ReturnsNull_ForAProjectThatWasNeverSaved()
    {
        await using var store = await OpenAsync();

        var loaded = await store.GetCodeProjectAsync(Guid.NewGuid());

        Assert.Null(loaded);
    }

    [Fact]
    public async Task ListCodeProjectsAsync_ReturnsAllSavedProjects_MostRecentlyUpdatedFirst()
    {
        await using var store = await OpenAsync();
        var first = await store.SaveCodeProjectAsync(NewProject(name: "first"));
        await Task.Delay(10);
        var second = await store.SaveCodeProjectAsync(NewProject(name: "second"));

        var list = await store.ListCodeProjectsAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal(second.Id, list[0].Id);
        Assert.Equal(first.Id, list[1].Id);
    }

    [Fact]
    public async Task SaveCodeProjectAsync_OnAnExistingId_UpsertsInPlace_PreservingOriginalCreatedAt()
    {
        await using var store = await OpenAsync();
        var original = await store.SaveCodeProjectAsync(NewProject(name: "v1"));

        await Task.Delay(10);
        var updated = await store.SaveCodeProjectAsync(original with { Name = "v2" });

        Assert.Equal("v2", updated.Name);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > original.UpdatedAt);
        var all = await store.ListCodeProjectsAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task SaveCodeProjectAsync_ReplacesFiles_RatherThanAccumulatingThem()
    {
        await using var store = await OpenAsync();
        var id = Guid.NewGuid();
        await store.SaveCodeProjectAsync(NewProject(id) with
        {
            Files = [new CodeFileDto("a.py", "python", "a"), new CodeFileDto("b.py", "python", "b")],
        });

        await store.SaveCodeProjectAsync(NewProject(id) with { Files = [new CodeFileDto("c.py", "python", "c")] });
        var loaded = await store.GetCodeProjectAsync(id);

        var file = Assert.Single(loaded!.Files);
        Assert.Equal("c.py", file.Path);
    }

    [Fact]
    public async Task DeleteCodeProjectAsync_RemovesTheProjectAndItsFiles()
    {
        await using var store = await OpenAsync();
        var project = await store.SaveCodeProjectAsync(NewProject());

        await store.DeleteCodeProjectAsync(project.Id);

        Assert.Null(await store.GetCodeProjectAsync(project.Id));
        Assert.Empty(await store.ListCodeProjectsAsync());
    }

    [Fact]
    public async Task OpenAsync_WhenTheKeystoreFails_ThrowsLocalStoreUnavailable_NotAPlaintextFallback()
    {
        var keyStore = new FakeSecureKeyStore { ThrowOnAccess = true };

        await Assert.ThrowsAsync<LocalStoreUnavailableException>(() => OpenAsync(keyStore));
        // The whole point of failing loudly here: no database file should be left behind
        // for a caller to accidentally open unencrypted later.
        Assert.False(File.Exists(_dbPath));
    }

    [Fact]
    public async Task OpenAsync_WithTheWrongPassphrase_ThrowsLocalStoreUnavailable()
    {
        await using (var store = await OpenAsync())
        {
            await store.SaveCodeProjectAsync(NewProject());
        }

        // A different passphrase for the same file — same failure mode SQLCipher itself
        // produces for a genuinely wrong key, not one this test fabricates a code path for.
        var differentPassphrase = new PreSeededKeyStore(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        await Assert.ThrowsAsync<LocalStoreUnavailableException>(
            () => LocalStoreContext.OpenAsync(differentPassphrase, _dbPath));
    }

    // Work Item C's core requirement: the on-disk file must be genuinely encrypted, not
    // just SQLite-with-an-obscure-extension. SQLCipher encrypts the entire file including
    // the header, so a real SQLite file's fixed 16-byte magic string ("SQLite format 3\0")
    // must NOT appear, and neither should any plaintext content the test wrote.
    [Fact]
    public async Task SaveCodeProjectAsync_TheOnDiskFile_IsNotPlaintextSqliteAndDoesNotContainSavedContent()
    {
        var marker = "TOTALLY-UNENCRYPTED-MARKER-STRING-98765";
        await using (var store = await OpenAsync())
        {
            await store.SaveCodeProjectAsync(NewProject() with
            {
                Files = [new CodeFileDto("secret.py", "python", marker)],
            });
        }

        var bytes = await File.ReadAllBytesAsync(_dbPath);
        var sqliteMagic = "SQLite format 3\0"u8.ToArray();
        Assert.False(bytes.AsSpan(0, sqliteMagic.Length).SequenceEqual(sqliteMagic));

        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(marker, text);
    }

    private sealed class PreSeededKeyStore(byte[] value) : ISecureKeyStore
    {
        public Task SetAsync(string key, byte[] v) => Task.CompletedTask;
        public Task<byte[]?> GetAsync(string key) => Task.FromResult<byte[]?>(value);
        public Task DeleteAsync(string key) => Task.CompletedTask;
    }
}
