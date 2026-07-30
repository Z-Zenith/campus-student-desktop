using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace StudentDesktop.Services;

// 0.3 spike (SDA/SEK plan): macOS implementation of ISecureKeyStore via the `security`
// CLI (ships with every macOS install, wraps Keychain Services) rather than raw P/Invoke
// against Security.framework's CFDictionary-based API — CoreFoundation interop
// (CFStringCreate/CFDictionaryCreate/etc.) is real complexity for marginal benefit here,
// and shelling out to a CLI matches this codebase's existing convention (e.g.
// ContainerCodeRunner shelling out to docker/podman rather than an engine API dependency).
//
// UNVERIFIED — no macOS available in this dev environment to actually test against (see
// the 0.3 spike's own scope note: ship Windows-verified first, macOS/Linux best-effort).
// Confirm this works on a real Mac before relying on it.
[SupportedOSPlatform("macos")]
public sealed class KeychainKeyStore : ISecureKeyStore
{
    // Keychain's generic-password items are scoped by (service, account) — service
    // identifies "which app," account identifies "which key within that app."
    private const string Service = "com.campus.studentdesktop";

    public async Task SetAsync(string key, byte[] value)
    {
        // add-generic-password has no "upsert" flag; -U updates in place if the
        // (service, account) pair already exists instead of erroring on a duplicate.
        var base64 = Convert.ToBase64String(value);
        await RunSecurityAsync(["add-generic-password", "-U", "-s", Service, "-a", key, "-w", base64]);
    }

    public async Task<byte[]?> GetAsync(string key)
    {
        var (exitCode, stdout) = await RunSecurityAsync(["find-generic-password", "-s", Service, "-a", key, "-w"]);
        if (exitCode != 0)
        {
            return null;
        }
        try
        {
            return Convert.FromBase64String(stdout.Trim());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key)
    {
        await RunSecurityAsync(["delete-generic-password", "-s", Service, "-a", key]);
    }

    private static async Task<(int ExitCode, string Stdout)> RunSecurityAsync(string[] args)
    {
        var psi = new ProcessStartInfo("security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start security process.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout);
    }
}
