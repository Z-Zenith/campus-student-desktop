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

// SDA-03/SDA-04: whitelisted browser. SDA-08: clip the current page into a note.
//
// The WebView control itself lives in BrowserView.axaml (a UI concern) — this ViewModel
// stays testable/UI-agnostic by exposing GetPageTitleAsync/GetSelectedTextAsync as
// delegates the View's code-behind wires up to the actual WebView's InvokeScript calls,
// rather than this ViewModel holding a reference to an Avalonia control.
public partial class BrowserViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private List<string> _whitelistedHosts = [];

    // Wired by BrowserView's code-behind to the real WebView. Left null-safe so this
    // ViewModel can be constructed and exercised without a live WebView (e.g. in tests).
    public Func<Task<string?>>? GetPageTitleAsync { get; set; }
    public Func<Task<string?>>? GetSelectedTextAsync { get; set; }

    [ObservableProperty]
    private string _urlInput = "";

    [ObservableProperty]
    private Uri? _currentSource;

    [ObservableProperty]
    private string? _errorMessage;

    // SDA-04: set alongside ErrorMessage whenever Navigate() blocks a non-whitelisted
    // site, so the "Request this site" button knows what to request and can hide itself
    // once the block clears (successful navigation, or a different error).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestWhitelistCommand))]
    private Uri? _blockedUri;

    [ObservableProperty]
    private string? _whitelistRequestMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestWhitelistCommand))]
    private bool _isWhitelistRequestBusy;

    [ObservableProperty]
    private bool _isClipPanelOpen;

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
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
    }

    [RelayCommand]
    private void Navigate()
    {
        ErrorMessage = null;
        BlockedUri = null;
        WhitelistRequestMessage = null;
        if (!Uri.TryCreate(UrlInput.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ErrorMessage = "Enter a valid http:// or https:// address.";
            return;
        }
        if (!IsWhitelisted(uri))
        {
            ErrorMessage = $"\"{uri.Host}\" is not on the whitelist. Ask a teacher to request access.";
            BlockedUri = uri;
            return;
        }
        CurrentSource = uri;
    }

    private bool CanRequestWhitelist() => BlockedUri is not null && !IsWhitelistRequestBusy;

    // SDA-04: student-initiated request for a site not yet on the whitelist. The backend
    // treats a duplicate pending request for the same URL as a no-op (returns the existing
    // one), so it's safe to call again if the student navigates away and back.
    [RelayCommand(CanExecute = nameof(CanRequestWhitelist))]
    private async Task RequestWhitelistAsync()
    {
        if (BlockedUri is not { } uri)
        {
            return;
        }

        IsWhitelistRequestBusy = true;
        WhitelistRequestMessage = null;
        try
        {
            await _apiClient.RequestWhitelistAsync(uri.ToString());
            WhitelistRequestMessage = "Request sent. A teacher will need to approve it.";
        }
        catch (ApiException ex)
        {
            WhitelistRequestMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            WhitelistRequestMessage = "Could not reach the server. Check your connection and try again.";
        }
        finally
        {
            IsWhitelistRequestBusy = false;
        }
    }

    // Called by BrowserView's NavigationStarted handler for every navigation the WebView
    // itself initiates (link clicks, redirects) — not just the ones this ViewModel
    // triggered via Navigate(), since those also need the same enforcement.
    public bool IsWhitelisted(Uri uri) => _whitelistedHosts.Contains(uri.Host.ToLowerInvariant());

    [RelayCommand]
    private async Task OpenClipPanelAsync()
    {
        if (CurrentSource is null)
        {
            ErrorMessage = "Navigate to a whitelisted page first.";
            return;
        }

        ClipErrorMessage = null;
        ClipSuccessMessage = null;
        IsNewNote = true;
        SelectedExistingNote = null;

        var title = GetPageTitleAsync is not null ? await GetPageTitleAsync() : null;
        ClipNoteTitle = string.IsNullOrWhiteSpace(title) ? CurrentSource.Host : title;

        var selection = GetSelectedTextAsync is not null ? await GetSelectedTextAsync() : null;
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

    private bool CanSaveClip() => !IsClipBusy;

    // SDA-08: acceptance-critical — "clipped content retains source URL as a reference".
    [RelayCommand(CanExecute = nameof(CanSaveClip))]
    private async Task SaveClipAsync()
    {
        if (CurrentSource is null)
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

        var clipBlock = $"> Clipped from [{ClipNoteTitle}]({CurrentSource})\n\n{ClipContent}".TrimEnd();

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
