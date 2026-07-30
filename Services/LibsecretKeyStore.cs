using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace StudentDesktop.Services;

// 0.3 spike (SDA/SEK plan): Linux implementation of ISecureKeyStore via the `secret-tool`
// CLI (part of libsecret-tools, talks to the org.freedesktop.secrets D-Bus service —
// GNOME Keyring, KWallet's Secret Service compat, etc.) rather than raw D-Bus protocol
// implementation, which is real complexity for marginal benefit — same CLI-over-native-API
// reasoning as KeychainKeyStore.
//
// UNVERIFIED — no Linux available in this dev environment to actually test against (see
// the 0.3 spike's own scope note: ship Windows-verified first, macOS/Linux best-effort).
//
// Documented weaker fallback: a headless/minimal Linux install (common on a school lab
// machine) may have no Secret Service daemon running at all, in which case secret-tool
// itself fails. Rather than that meaning "no local storage is possible on this machine,"
// fall back to a plain file with restrictive owner-only permissions (chmod 600) —
// genuinely weaker than a real OS keystore (readable by anyone with root or physical
// access to the user's own files, unlike a proper keyring), but still not world-readable,
// and explicitly logged/flagged rather than silently degrading unnoticed.
[SupportedOSPlatform("linux")]
public sealed class LibsecretKeyStore : ISecureKeyStore
{
    private const string Attribute = "campus-student-desktop-key";
    private static readonly string FallbackDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudentDesktop", "keystore-fallback");

    public async Task SetAsync(string key, byte[] value)
    {
        var base64 = Convert.ToBase64String(value);
        var (exitCode, _) = await RunSecretToolAsync(
            ["store", "--label", $"Campus Student Desktop: {key}", Attribute, key], stdin: base64);
        if (exitCode != 0)
        {
            // No logging framework exists anywhere in this codebase (no DI container —
            // see ThemeService's own doc comment) — Debug.WriteLine is the same
            // diagnostic-visibility level this app already uses elsewhere for
            // non-fatal/best-effort conditions, not a new convention.
            System.Diagnostics.Debug.WriteLine(
                $"secret-tool unavailable (no Secret Service daemon?) — falling back to a restrictive-permission " +
                $"file for key '{key}'. This is a weaker guarantee than a real OS keystore.");
            WriteFallbackFile(key, value);
        }
    }

    public async Task<byte[]?> GetAsync(string key)
    {
        var (exitCode, stdout) = await RunSecretToolAsync(["lookup", Attribute, key], stdin: null);
        if (exitCode == 0)
        {
            try
            {
                return Convert.FromBase64String(stdout.Trim());
            }
            catch (FormatException)
            {
                return null;
            }
        }
        return ReadFallbackFile(key);
    }

    public async Task DeleteAsync(string key)
    {
        await RunSecretToolAsync(["clear", Attribute, key], stdin: null);
        var fallbackPath = PathFor(key);
        if (File.Exists(fallbackPath))
        {
            File.Delete(fallbackPath);
        }
    }

    private static void WriteFallbackFile(string key, byte[] value)
    {
        Directory.CreateDirectory(FallbackDir);
        var path = PathFor(key);
        File.WriteAllBytes(path, value);
        // Owner read/write only (0600) — best available protection without a real
        // keystore daemon. chmod via File.SetUnixFileMode (.NET 7+), not a shell-out.
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static byte[]? ReadFallbackFile(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static string PathFor(string key) => Path.Combine(FallbackDir, SanitizeKey(key) + ".bin");

    private static string SanitizeKey(string key) =>
        new(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

    private static async Task<(int ExitCode, string Stdout)> RunSecretToolAsync(string[] args, string? stdin)
    {
        try
        {
            var psi = new ProcessStartInfo("secret-tool")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start secret-tool process.");
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin);
                process.StandardInput.Close();
            }
            var stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, stdout);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // secret-tool not installed at all.
            return (-1, "");
        }
    }
}
