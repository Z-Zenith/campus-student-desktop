using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using StudentDesktop.Models;

namespace StudentDesktop.Services;

// Thrown when the OS secure keystore is genuinely unavailable (see ISecureKeyStore) —
// LocalStoreContext fails loudly rather than silently falling back to an unencrypted DB
// or a hardcoded key, matching this codebase's existing convention of refusing to
// silently loosen a security guarantee (e.g. ContainerCodeRunner's fail-fast when no
// runtime is available, rather than pretending sandboxing still applies).
public sealed class LocalStoreUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

// Work Item C (SDA/SEK plan): SQLCipher-backed local store for scratch/working code
// projects — these never sync to the backend. Mirrors the *shape* of the server's
// code_projects/code_files tables (schema doc §1.9), not a literal copy: no owner_id/
// college-scoping needed for a single-user local store. Keeps the same client-generated
// Guid id scheme SEK already uses (crypto.randomUUID), so a local project and its
// eventually-submitted copy (see the Submit-as-assignment action) share identity.
public sealed class LocalStoreContext : IAsyncDisposable, ILocalStore
{
    private const string PassphraseKeyName = "local-store-passphrase";
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudentDesktop", "local-store.db");

    private readonly string _connectionString;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _schemaEnsured;

    private LocalStoreContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// dbPath overrides the default per-user file location — exposed for tests that need
    /// an isolated, disposable database file rather than the one real shared location.
    public static async Task<LocalStoreContext> OpenAsync(ISecureKeyStore keyStore, string? dbPath = null)
    {
        // Explicit provider selection: Microsoft.Data.Sqlite's own transitive dependencies
        // can otherwise pull in the plain (unencrypted) e_sqlite3 provider alongside
        // SQLitePCLRaw.bundle_e_sqlcipher's e_sqlcipher one, and whichever registers last
        // silently wins — forcing e_sqlcipher here is what actually guarantees encryption
        // is in effect, not just present in the dependency tree.
        raw.SetProvider(new SQLite3Provider_e_sqlcipher());

        byte[] passphraseBytes;
        try
        {
            passphraseBytes = await keyStore.GetAsync(PassphraseKeyName) ?? await GenerateAndStorePassphraseAsync(keyStore);
        }
        catch (Exception ex) when (ex is not LocalStoreUnavailableException)
        {
            // Deliberately not caught more narrowly (e.g. just PlatformNotSupportedException)
            // — ANY failure reading/writing the OS keystore means this store's encryption
            // guarantee can't be trusted, and the fail-loud contract applies uniformly.
            throw new LocalStoreUnavailableException(
                "Local scratch storage is unavailable: the OS secure keystore could not be reached.", ex);
        }

        var resolvedPath = dbPath ?? DbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = resolvedPath,
            Password = Convert.ToBase64String(passphraseBytes),
            // Pooling keeps the native sqlite3 handle (and the OS file lock) alive past a
            // SqliteConnection's own Dispose() — harmless in the running app, but it means
            // a test that saves, disposes, then immediately reads the raw file bytes (or
            // deletes it) can race a pooled handle that hasn't actually let go yet. Each
            // connection here is already short-lived and per-call, not held open, so
            // there's no real throughput cost to disabling the pool.
            Pooling = false,
        }.ToString();

        var context = new LocalStoreContext(connectionString);
        await context.EnsureSchemaAsync();
        return context;
    }

    private static async Task<byte[]> GenerateAndStorePassphraseAsync(ISecureKeyStore keyStore)
    {
        var passphrase = RandomNumberGenerator.GetBytes(32);
        await keyStore.SetAsync(PassphraseKeyName, passphrase);
        return passphrase;
    }

    private async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
        {
            return;
        }
        // A wrong/mismatched key can surface as early as Open() itself (Microsoft.Data.Sqlite
        // issues `PRAGMA key` as part of opening the connection) or, for some SQLCipher
        // versions, only on the first real read — wrapping both in one try/catch surfaces
        // either case as the same LocalStoreUnavailableException instead of leaking a raw
        // "file is not a database" SqliteException the first time a caller tries to save.
        await using var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS code_projects (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    entry_file_path TEXT NOT NULL,
                    active_file_path TEXT NOT NULL,
                    stdin TEXT,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS code_files (
                    project_id TEXT NOT NULL REFERENCES code_projects(id) ON DELETE CASCADE,
                    path TEXT NOT NULL,
                    language TEXT NOT NULL,
                    content TEXT NOT NULL,
                    PRIMARY KEY (project_id, path)
                );
                """;
            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException ex)
        {
            throw new LocalStoreUnavailableException(
                "Local scratch storage is unavailable: the local database could not be opened with the stored key.", ex);
        }

        _schemaEnsured = true;
    }

    public async Task<IReadOnlyList<CodeProjectSummaryDto>> ListCodeProjectsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, name, updated_at FROM code_projects ORDER BY updated_at DESC";

            var results = new List<CodeProjectSummaryDto>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new CodeProjectSummaryDto(
                    Guid.Parse(reader.GetString(0)), reader.GetString(1), DateTime.Parse(reader.GetString(2)).ToUniversalTime()));
            }
            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CodeProjectDto?> GetCodeProjectAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var projectCommand = connection.CreateCommand();
            projectCommand.CommandText =
                "SELECT name, entry_file_path, active_file_path, stdin, created_at, updated_at FROM code_projects WHERE id = $id";
            projectCommand.Parameters.AddWithValue("$id", id.ToString());

            await using var projectReader = await projectCommand.ExecuteReaderAsync();
            if (!await projectReader.ReadAsync())
            {
                return null;
            }
            var name = projectReader.GetString(0);
            var entryFilePath = projectReader.GetString(1);
            var activeFilePath = projectReader.GetString(2);
            var stdin = projectReader.IsDBNull(3) ? null : projectReader.GetString(3);
            var createdAt = DateTime.Parse(projectReader.GetString(4)).ToUniversalTime();
            var updatedAt = DateTime.Parse(projectReader.GetString(5)).ToUniversalTime();
            await projectReader.DisposeAsync();

            await using var filesCommand = connection.CreateCommand();
            filesCommand.CommandText = "SELECT path, language, content FROM code_files WHERE project_id = $id ORDER BY path";
            filesCommand.Parameters.AddWithValue("$id", id.ToString());

            var files = new List<CodeFileDto>();
            await using var filesReader = await filesCommand.ExecuteReaderAsync();
            while (await filesReader.ReadAsync())
            {
                files.Add(new CodeFileDto(filesReader.GetString(0), filesReader.GetString(1), filesReader.GetString(2)));
            }

            return new CodeProjectDto(id, name, files, entryFilePath, activeFilePath, stdin, createdAt, updatedAt);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CodeProjectDto> SaveCodeProjectAsync(CodeProjectDto project)
    {
        await _lock.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            var now = DateTime.UtcNow;
            var existingCreatedAt = await GetExistingCreatedAtAsync(connection, transaction, project.Id);
            var createdAt = existingCreatedAt ?? now;

            await using (var upsertProject = connection.CreateCommand())
            {
                upsertProject.Transaction = transaction;
                upsertProject.CommandText = """
                    INSERT INTO code_projects (id, name, entry_file_path, active_file_path, stdin, created_at, updated_at)
                    VALUES ($id, $name, $entry, $active, $stdin, $created, $updated)
                    ON CONFLICT(id) DO UPDATE SET
                        name = excluded.name, entry_file_path = excluded.entry_file_path,
                        active_file_path = excluded.active_file_path, stdin = excluded.stdin, updated_at = excluded.updated_at
                    """;
                upsertProject.Parameters.AddWithValue("$id", project.Id.ToString());
                upsertProject.Parameters.AddWithValue("$name", project.Name);
                upsertProject.Parameters.AddWithValue("$entry", project.EntryFilePath);
                upsertProject.Parameters.AddWithValue("$active", project.ActiveFilePath);
                upsertProject.Parameters.AddWithValue("$stdin", (object?)project.Stdin ?? DBNull.Value);
                upsertProject.Parameters.AddWithValue("$created", createdAt.ToString("O"));
                upsertProject.Parameters.AddWithValue("$updated", now.ToString("O"));
                await upsertProject.ExecuteNonQueryAsync();
            }

            await using (var deleteFiles = connection.CreateCommand())
            {
                deleteFiles.Transaction = transaction;
                deleteFiles.CommandText = "DELETE FROM code_files WHERE project_id = $id";
                deleteFiles.Parameters.AddWithValue("$id", project.Id.ToString());
                await deleteFiles.ExecuteNonQueryAsync();
            }

            foreach (var file in project.Files)
            {
                await using var insertFile = connection.CreateCommand();
                insertFile.Transaction = transaction;
                insertFile.CommandText =
                    "INSERT INTO code_files (project_id, path, language, content) VALUES ($id, $path, $lang, $content)";
                insertFile.Parameters.AddWithValue("$id", project.Id.ToString());
                insertFile.Parameters.AddWithValue("$path", file.Path);
                insertFile.Parameters.AddWithValue("$lang", file.Language);
                insertFile.Parameters.AddWithValue("$content", file.Content);
                await insertFile.ExecuteNonQueryAsync();
            }

            transaction.Commit();
            return project with { CreatedAt = createdAt, UpdatedAt = now };
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<DateTime?> GetExistingCreatedAtAsync(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT created_at FROM code_projects WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        var result = await command.ExecuteScalarAsync();
        return result is string s ? DateTime.Parse(s).ToUniversalTime() : null;
    }

    public async Task DeleteCodeProjectAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // code_files' FK is declared ON DELETE CASCADE, but SQLite only enforces
            // foreign keys when PRAGMA foreign_keys is on (off by default per connection)
            // — deleting explicitly rather than depending on that pragma being set.
            command.CommandText = "DELETE FROM code_files WHERE project_id = $id; DELETE FROM code_projects WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
