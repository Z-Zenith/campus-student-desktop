using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StudentDesktop.Services;

// Wraps Application.Current.RequestedThemeVariant (hardcoded to "Default" in App.axaml
// before this existed) behind a small toggle, persisted across restarts. There's no DI
// container in this app — constructed once in MainWindowViewModel and threaded down to
// ShellViewModel, same as ApiClient/AssignmentAutoSubmitService.
public partial class ThemeService : ObservableObject
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StudentDesktop", "theme.json");

    [ObservableProperty]
    private bool _isDarkMode;

    public ThemeService()
    {
        _isDarkMode = LoadIsDarkMode();
        Apply();
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        Apply();
        Save(value);
    }

    private void Apply()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    private static bool LoadIsDarkMode()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return false;
            }
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<ThemeSettings>(json);
            return settings?.IsDarkMode ?? false;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt/unreadable preference file just falls back to light mode — not
            // worth surfacing an error for a cosmetic setting.
            return false;
        }
    }

    private static void Save(bool isDarkMode)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new ThemeSettings(isDarkMode)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort — the toggle still works for the rest of this session even if
            // the preference can't be persisted.
        }
    }

    private sealed record ThemeSettings(bool IsDarkMode);
}
