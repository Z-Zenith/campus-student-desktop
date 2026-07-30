using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SDA-03/SDA-04: one open browser tab. Holds the per-page navigation/classification state
// that used to live directly on BrowserViewModel before Chrome-style multi-tab support —
// this is a relocation of that logic, not a rewrite (see BrowserViewModelTests.cs for the
// scenarios that must still hold). BrowserViewModel is now the tab container: it owns the
// shared, college-wide whitelist/classification chain and hands this tab two narrow
// delegates rather than an ApiClient directly, so a tab is testable standalone with no
// HTTP dependency.
//
// The WebView control itself lives in BrowserView.axaml (a UI concern) — this ViewModel
// stays testable/UI-agnostic by exposing GetPageTitleAsync/GetSelectedTextAsync as
// delegates the View's code-behind wires up to this tab's actual NativeWebView instance.
public partial class BrowserTabViewModel : ViewModelBase
{
    // SDA-03 classification policy engine (Work Item A2): async because a classify call
    // may need a network round trip — the one real breaking change from the old
    // synchronous Func<Uri, bool> IsWhitelisted delegate. The desktop client trusts
    // whatever NavigationDecision this returns; it never computes its own allow/deny.
    private readonly Func<Uri, Task<NavigationDecision>> _classifyAsync;
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

    // SDA-03: distinct from IsLoading (page content loading) — this covers the classify
    // round trip itself, so the address bar can show "checking..." rather than freezing
    // with no feedback during the async gate.
    [ObservableProperty]
    private bool _isCheckingNavigation;

    // Doubles as the tab strip's label ("New Tab" until the first navigation completes) —
    // NotifyPropertyChangedFor is required here since DisplayTitle is a hand-written
    // computed property, not its own [ObservableProperty]; without it, the tab strip's
    // binding to DisplayTitle never refreshes when PageTitle changes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string? _pageTitle;

    [ObservableProperty]
    private string? _errorMessage;

    // SDA-04: set alongside ErrorMessage whenever Navigate() blocks a site (not on the
    // whitelist, or the classifier denied it), so the "Request this site" button knows
    // what to request and can hide itself once the block clears (successful navigation,
    // or a different error). Deliberately NOT set for a NavigationDecisionKind.Error
    // (classifier unreachable) — "couldn't verify" isn't the same as "verified and
    // blocked," and shouldn't offer a whitelist-request action for a site that was never
    // actually evaluated.
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
        Func<Uri, Task<NavigationDecision>> classifyAsync,
        Func<Uri, Task<string>> requestWhitelistAsync,
        Action<BrowserTabViewModel>? onClose = null)
    {
        _classifyAsync = classifyAsync;
        _requestWhitelistAsync = requestWhitelistAsync;
        _onClose = onClose;
    }

    // SDA-03: Chrome-style omnibox — a typed value that isn't itself an absolute
    // http(s) URL or a bare-domain-looking string routes through a Google search
    // instead of being rejected outright. The resulting URI (direct or search) still
    // has to clear the classification gate like any other navigation — search is a way
    // to find allowed sites faster, not a way around the gate.
    [RelayCommand]
    private async Task NavigateAsync()
    {
        ErrorMessage = null;
        BlockedUri = null;
        WhitelistRequestMessage = null;

        if (string.IsNullOrWhiteSpace(UrlInput))
        {
            ErrorMessage = "Enter an address or search term.";
            return;
        }

        var uri = UrlBarInterpreter.Resolve(UrlInput).TargetUri;

        IsCheckingNavigation = true;
        NavigationDecision decision;
        try
        {
            decision = await _classifyAsync(uri);
        }
        finally
        {
            IsCheckingNavigation = false;
        }

        ApplyDecision(uri, decision);
    }

    // Shared by NavigateAsync (omnibox) and BrowserView's OnNavigationStarted (in-page
    // link clicks/redirects) — both funnel through the same decision-application logic
    // rather than duplicating the Allowed/Blocked/Error handling in the View's code-behind.
    public void ApplyDecision(Uri uri, NavigationDecision decision)
    {
        switch (decision.Kind)
        {
            case NavigationDecisionKind.Allowed:
                CurrentSource = uri;
                break;
            case NavigationDecisionKind.Blocked:
                ErrorMessage = decision.Message ?? $"\"{uri.Host}\" is not allowed. Ask a teacher to request access.";
                BlockedUri = uri;
                break;
            case NavigationDecisionKind.Error:
                // Fail closed here too — same philosophy as a Blocked decision (navigation
                // does not proceed) — but the message must let a student/teacher tell
                // "the system is down" from "this is genuinely blocked," and no
                // "Request this site" action is offered since nothing was actually
                // evaluated.
                ErrorMessage = decision.Message ?? "Could not verify this site. Check your connection and try again.";
                break;
        }
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
    // triggered via NavigateAsync(), since those also need the same enforcement.
    public Task<NavigationDecision> ClassifyNavigationAsync(Uri uri) => _classifyAsync(uri);

    [RelayCommand]
    private void Close() => _onClose?.Invoke(this);
}
