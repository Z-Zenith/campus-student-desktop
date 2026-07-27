using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudentDesktop.ViewModels;

// SDA-03/SDA-04: one open browser tab. Holds the per-page navigation/whitelist state
// that used to live directly on BrowserViewModel before Chrome-style multi-tab support —
// this is a relocation of that logic, not a rewrite (see BrowserViewModelTests.cs for the
// scenarios that must still hold). BrowserViewModel is now the tab container: it owns the
// shared, college-wide whitelist and hands this tab two narrow delegates rather than an
// ApiClient directly, so a tab is testable standalone with no HTTP dependency.
//
// The WebView control itself lives in BrowserView.axaml (a UI concern) — this ViewModel
// stays testable/UI-agnostic by exposing GetPageTitleAsync/GetSelectedTextAsync as
// delegates the View's code-behind wires up to this tab's actual NativeWebView instance.
public partial class BrowserTabViewModel : ViewModelBase
{
    private readonly Func<Uri, bool> _isWhitelisted;
    private readonly Func<Uri, Task<string>> _requestWhitelistAsync;
    private readonly Action<BrowserTabViewModel>? _onClose;

    public Guid Id { get; } = Guid.NewGuid();

    // Wired by BrowserView's code-behind to this tab's own NativeWebView instance.
    public Func<Task<string?>>? GetPageTitleAsync { get; set; }
    public Func<Task<string?>>? GetSelectedTextAsync { get; set; }
    public Action? GoBackRequested { get; set; }
    public Action? GoForwardRequested { get; set; }
    public Action? ReloadRequested { get; set; }
    public Func<bool>? CanGoBack { get; set; }
    public Func<bool>? CanGoForward { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _urlInput = "";

    [ObservableProperty]
    private Uri? _currentSource;

    [ObservableProperty]
    private bool _isLoading;

    // Doubles as the tab strip's label ("New Tab" until the first navigation completes) —
    // NotifyPropertyChangedFor is required here since DisplayTitle is a hand-written
    // computed property, not its own [ObservableProperty]; without it, the tab strip's
    // binding to DisplayTitle never refreshes when PageTitle changes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string? _pageTitle;

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

    public string DisplayTitle => string.IsNullOrWhiteSpace(PageTitle) ? "New Tab" : PageTitle;

    public BrowserTabViewModel(
        Func<Uri, bool> isWhitelisted,
        Func<Uri, Task<string>> requestWhitelistAsync,
        Action<BrowserTabViewModel>? onClose = null)
    {
        _isWhitelisted = isWhitelisted;
        _requestWhitelistAsync = requestWhitelistAsync;
        _onClose = onClose;
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

    [RelayCommand(CanExecute = nameof(CanGoBackNow))]
    private void GoBack() => GoBackRequested?.Invoke();

    private bool CanGoBackNow() => CanGoBack?.Invoke() ?? false;

    [RelayCommand(CanExecute = nameof(CanGoForwardNow))]
    private void GoForward() => GoForwardRequested?.Invoke();

    private bool CanGoForwardNow() => CanGoForward?.Invoke() ?? false;

    [RelayCommand]
    private void Reload() => ReloadRequested?.Invoke();

    // Called by BrowserView's code-behind after every navigation completes, since
    // CanGoBack/CanGoForward availability only changes at that point.
    public void RefreshNavigationState()
    {
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
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
            WhitelistRequestMessage = await _requestWhitelistAsync(uri);
        }
        catch (Exception ex)
        {
            WhitelistRequestMessage = ex.Message;
        }
        finally
        {
            IsWhitelistRequestBusy = false;
        }
    }

    // Called by BrowserView's NavigationStarted handler for every navigation the WebView
    // itself initiates (link clicks, redirects) — not just the ones this ViewModel
    // triggered via Navigate(), since those also need the same enforcement.
    public bool IsWhitelisted(Uri uri) => _isWhitelisted(uri);

    [RelayCommand]
    private void Close() => _onClose?.Invoke(this);
}
