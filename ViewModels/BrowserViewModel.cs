using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SDA-03/SDA-04: whitelisted, Chrome-style multi-tab browser. SDA-08: clip the current
// (active tab's) page into a note.
//
// Per-page navigation/whitelist state lives on BrowserTabViewModel — this VM is the tab
// container: it owns the shared, college-wide whitelist (loaded once, not per-tab) and
// hands each tab two narrow delegates (IsWhitelisted / RequestWhitelistAsync) rather than
// an ApiClient reference, so BrowserTabViewModel stays testable standalone.
public partial class BrowserViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private List<string> _whitelistedHosts = [];

    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private BrowserTabViewModel? _selectedTab;

    [ObservableProperty]
    private string? _whitelistLoadError;

    [ObservableProperty]
    private bool _isClipPanelOpen;

    [ObservableProperty]
    private bool _isAllowedSitesPanelOpen;

    // Read-only view of the same college-wide whitelist backing IsWhitelisted's host-set
    // check above — kept as full WhitelistSiteDtos (not just hosts) so the panel can show
    // the approved URL and date, not just a bare host.
    public ObservableCollection<WhitelistSiteDto> AllowedSites { get; } = [];

    [ObservableProperty]
    private bool _isNewNote = true;

    [ObservableProperty]
    private string _clipNoteTitle = "";

    [ObservableProperty]
    private string _clipContent = "";

    [ObservableProperty]
    private NoteSummaryDto? _selectedExistingNote;

    [ObservableProperty]
    private string? _clipErrorMessage;

    [ObservableProperty]
    private string? _clipSuccessMessage;

    [ObservableProperty]
    private bool _isClipBusy;

    public ObservableCollection<NoteSummaryDto> ExistingNotes { get; } = [];

    public BrowserViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
        _ = LoadWhitelistAsync();
        // A browser always opens with exactly one tab, like a real browser window —
        // closing the last remaining tab resets to a fresh blank one rather than ever
        // leaving zero tabs (Browser is a persistent app-tab in Shell, not a closable
        // window of its own).
        NewTab();
    }

    [RelayCommand]
    private async Task LoadWhitelistAsync()
    {
        try
        {
            var whitelist = await _apiClient.GetWhitelistAsync();
            _whitelistedHosts = whitelist.Sites
                .Select(s => TryGetHost(s.Url))
                .Where(host => host is not null)
                .Select(host => host!)
                .Distinct()
                .ToList();

            AllowedSites.Clear();
            foreach (var site in whitelist.Sites.OrderBy(s => s.Url))
            {
                AllowedSites.Add(site);
            }
        }
        catch (ApiException ex)
        {
            WhitelistLoadError = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            WhitelistLoadError = "Could not reach the server. Check your connection and try again.";
        }
    }

    // Passed into each BrowserTabViewModel as a delegate — the exact same fail-closed
    // exact-host-match check every tab shared before this VM had per-tab navigation state.
    public bool IsWhitelisted(Uri uri) => _whitelistedHosts.Contains(uri.Host.ToLowerInvariant());

    // Passed into each BrowserTabViewModel as a delegate. Never throws — always resolves
    // to the message the tab should display, preserving the exact same wording the old
    // single-tab RequestWhitelistAsync produced for each outcome.
    private async Task<string> RequestWhitelistAsync(Uri uri)
    {
        try
        {
            await _apiClient.RequestWhitelistAsync(uri.ToString());
            return "Request sent. A teacher will need to approve it.";
        }
        catch (ApiException ex)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Could not reach the server. Check your connection and try again.";
        }
    }

    [RelayCommand]
    private void NewTab()
    {
        var tab = new BrowserTabViewModel(IsWhitelisted, RequestWhitelistAsync, OnTabClosed);
        Tabs.Add(tab);
        SelectTab(tab);
    }

    [RelayCommand]
    private void SelectTab(BrowserTabViewModel tab)
    {
        if (SelectedTab is not null)
        {
            SelectedTab.IsSelected = false;
        }
        SelectedTab = tab;
        tab.IsSelected = true;
    }

    private void OnTabClosed(BrowserTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }
        Tabs.RemoveAt(index);

        if (Tabs.Count == 0)
        {
            NewTab();
            return;
        }
        if (SelectedTab == tab)
        {
            SelectTab(Tabs[Math.Min(index, Tabs.Count - 1)]);
        }
    }

    [RelayCommand]
    private async Task OpenClipPanelAsync()
    {
        var tab = SelectedTab;
        if (tab?.CurrentSource is null)
        {
            if (tab is not null)
            {
                tab.ErrorMessage = "Navigate to a whitelisted page first.";
            }
            return;
        }

        ClipErrorMessage = null;
        ClipSuccessMessage = null;
        IsNewNote = true;
        SelectedExistingNote = null;

        var title = tab.GetPageTitleAsync is not null ? await tab.GetPageTitleAsync() : null;
        ClipNoteTitle = string.IsNullOrWhiteSpace(title) ? tab.CurrentSource.Host : title;

        var selection = tab.GetSelectedTextAsync is not null ? await tab.GetSelectedTextAsync() : null;
        ClipContent = selection ?? "";

        try
        {
            var notes = await _apiClient.GetMyNotesAsync();
            ExistingNotes.Clear();
            foreach (var note in notes)
            {
                ExistingNotes.Add(note);
            }
        }
        catch (ApiException)
        {
            // Non-fatal — "append to existing" just won't have options; "new note" still works.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Same as above.
        }

        IsClipPanelOpen = true;
    }

    [RelayCommand]
    private void CloseClipPanel() => IsClipPanelOpen = false;

    [RelayCommand]
    private void OpenAllowedSitesPanel() => IsAllowedSitesPanelOpen = true;

    [RelayCommand]
    private void CloseAllowedSitesPanel() => IsAllowedSitesPanelOpen = false;

    private bool CanSaveClip() => !IsClipBusy;

    // SDA-08: acceptance-critical — "clipped content retains source URL as a reference".
    [RelayCommand(CanExecute = nameof(CanSaveClip))]
    private async Task SaveClipAsync()
    {
        var currentSource = SelectedTab?.CurrentSource;
        if (currentSource is null)
        {
            return;
        }
        if (IsNewNote && string.IsNullOrWhiteSpace(ClipNoteTitle))
        {
            ClipErrorMessage = "Enter a title for the new note.";
            return;
        }
        if (!IsNewNote && SelectedExistingNote is null)
        {
            ClipErrorMessage = "Choose a note to append to.";
            return;
        }

        var clipBlock = $"> Clipped from [{ClipNoteTitle}]({currentSource})\n\n{ClipContent}".TrimEnd();

        IsClipBusy = true;
        ClipErrorMessage = null;
        try
        {
            if (IsNewNote)
            {
                await _apiClient.CreateNoteAsync(ClipNoteTitle.Trim(), clipBlock);
            }
            else
            {
                // Mine() only returns title/updatedAt — fetch the full current content
                // first so this is a genuine append, not an overwrite.
                var existing = SelectedExistingNote!;
                var full = await _apiClient.GetNoteAsync(existing.Id);
                var appended = string.IsNullOrEmpty(full.ContentMarkdown)
                    ? clipBlock
                    : full.ContentMarkdown + "\n\n" + clipBlock;
                await _apiClient.UpdateNoteAsync(full.Id, full.Title, appended);
            }
            ClipSuccessMessage = "Clipped to notes.";
            IsClipPanelOpen = false;
        }
        catch (ApiException ex)
        {
            ClipErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ClipErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
        finally
        {
            IsClipBusy = false;
        }
    }

    private static string? TryGetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;
}
