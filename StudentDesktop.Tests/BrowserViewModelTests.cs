using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Tests;

// SDA-03/04's scheme/whitelist-rejection scenarios now live on BrowserTabViewModel
// (the per-tab unit), constructed directly with fake classifyAsync/requestWhitelistAsync
// delegates — no ApiClient/HTTP dependency needed for these anymore, unlike before the
// Chrome-style multi-tab refactor. BrowserViewModelTests below covers the container:
// initial-tab creation, tab management, and Clip-to-Notes (which stayed container-level).
public class BrowserTabViewModelTests
{
    // SDA-03 classification policy engine (Work Item A2): the async classify delegate
    // fully replaced the old synchronous Func<Uri, bool> IsWhitelisted one. This overload
    // keeps the simple bool-in-tests-out shape most existing tests want (allowed vs. not),
    // wrapping it into the real NavigationDecision-returning async shape — same "not on
    // the whitelist" message text the old synchronous check produced, so those assertions
    // didn't need to change.
    private static BrowserTabViewModel NewTab(Func<Uri, bool>? isWhitelisted = null, Func<Uri, Task<string>>? requestWhitelistAsync = null)
    {
        var isAllowed = isWhitelisted ?? (_ => false);
        return NewTab(
            classifyAsync: uri => Task.FromResult(isAllowed(uri)
                ? NavigationDecision.Allowed()
                : NavigationDecision.Blocked($"\"{uri.Host}\" is not on the whitelist. Ask a teacher to request access.")),
            requestWhitelistAsync: requestWhitelistAsync);
    }

    private static BrowserTabViewModel NewTab(Func<Uri, Task<NavigationDecision>> classifyAsync, Func<Uri, Task<string>>? requestWhitelistAsync = null) =>
        new(classifyAsync, requestWhitelistAsync ?? (_ => Task.FromResult("")));

    // Chrome-style omnibox: a non-http(s) scheme like "ftp://" isn't shaped like a bare
    // domain (it contains "/"), so it falls through to a Google search rather than being
    // rejected outright — same as typing it into Chrome's own address bar. It still has
    // to clear the whitelist gate like any other navigation.
    [Fact]
    public async Task Navigate_TreatsANonHttpSchemeAsASearchTerm_NotAnOutrightRejection()
    {
        var tab = NewTab(isWhitelisted: _ => true);
        tab.UrlInput = "ftp://example.com";

        await tab.NavigateCommand.ExecuteAsync(null);

        Assert.NotNull(tab.CurrentSource);
        Assert.Equal("www.google.com", tab.CurrentSource!.Host);
        Assert.Null(tab.ErrorMessage);
    }

    [Fact]
    public async Task Navigate_TreatsABareDomainAsHttps()
    {
        var tab = NewTab(isWhitelisted: _ => true);
        tab.UrlInput = "github.com";

        await tab.NavigateCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://github.com"), tab.CurrentSource);
    }

    [Fact]
    public async Task Navigate_TreatsASearchPhraseAsAGoogleSearch_StillGatedByTheWhitelist()
    {
        var tab = NewTab(isWhitelisted: uri => uri.Host == "www.google.com");
        tab.UrlInput = "how do i center a div";

        await tab.NavigateCommand.ExecuteAsync(null);

        Assert.NotNull(tab.CurrentSource);
        Assert.Equal("www.google.com", tab.CurrentSource!.Host);
        Assert.Contains("how%20do%20i%20center%20a%20div", tab.CurrentSource.Query);
    }

    [Fact]
    public async Task Navigate_EmptyInput_ShowsAnErrorRatherThanSearching()
    {
        var tab = NewTab(isWhitelisted: _ => true);
        tab.UrlInput = "   ";

        await tab.NavigateCommand.ExecuteAsync(null);

        Assert.Null(tab.CurrentSource);
        Assert.Equal("Enter an address or search term.", tab.ErrorMessage);
    }

    [Fact]
    public async Task SDA03_NavigateRejectsHostNotOnWhitelist()
    {
        var tab = NewTab(isWhitelisted: _ => false);
        tab.UrlInput = "https://not-whitelisted.example.com";

        await tab.NavigateCommand.ExecuteAsync(null);

        Assert.Null(tab.CurrentSource);
        Assert.Contains("not on the whitelist", tab.ErrorMessage);
    }

    [Fact]
    public async Task SDA04_NavigateBlockedSiteExposesItForRequest()
    {
        var tab = NewTab(isWhitelisted: _ => false);
        tab.UrlInput = "https://not-whitelisted.example.com";

        await tab.NavigateCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://not-whitelisted.example.com"), tab.BlockedUri);
        Assert.True(tab.RequestWhitelistCommand.CanExecute(null));
    }

    [Fact]
    public async Task SDA04_ASuccessfulOmniboxNavigation_DoesNotOfferAWhitelistRequest()
    {
        var tab = NewTab(isWhitelisted: _ => true);
        tab.UrlInput = "ftp://example.com";

        await tab.NavigateCommand.ExecuteAsync(null);

        Assert.Null(tab.BlockedUri);
        Assert.False(tab.RequestWhitelistCommand.CanExecute(null));
    }

    // SDA-03 classification policy engine (Work Item A2): "couldn't verify" (classifier
    // unreachable) must fail closed like a genuine Blocked decision (navigation doesn't
    // proceed), but with distinct messaging and no "Request this site" offer, since
    // nothing was actually evaluated.
    [Fact]
    public async Task Navigate_ClassifierError_FailsClosed_WithDistinctMessaging_AndNoWhitelistRequestOffer()
    {
        var tab = NewTab(classifyAsync: _ => Task.FromResult(NavigationDecision.Error("Could not verify this site. Check your connection and try again.")));
        tab.UrlInput = "https://example.com";

        await tab.NavigateCommand.ExecuteAsync(null);

        Assert.Null(tab.CurrentSource);
        Assert.Null(tab.BlockedUri);
        Assert.Equal("Could not verify this site. Check your connection and try again.", tab.ErrorMessage);
        Assert.False(tab.RequestWhitelistCommand.CanExecute(null));
    }

    [Fact]
    public async Task Navigate_SetsIsCheckingNavigation_WhileTheClassifyCallIsInFlight()
    {
        var gate = new TaskCompletionSource<NavigationDecision>();
        var tab = NewTab(classifyAsync: _ => gate.Task);
        tab.UrlInput = "https://example.com";

        var navigateTask = tab.NavigateCommand.ExecuteAsync(null);
        Assert.True(tab.IsCheckingNavigation);

        gate.SetResult(NavigationDecision.Allowed());
        await navigateTask;

        Assert.False(tab.IsCheckingNavigation);
    }

    [Fact]
    public void NewTabCommand_AddsAndSelectsANewTab()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"));
        var firstTab = viewModel.SelectedTab;

        viewModel.NewTabCommand.Execute(null);

        Assert.Equal(2, viewModel.Tabs.Count);
        Assert.NotSame(firstTab, viewModel.SelectedTab);
        Assert.True(viewModel.SelectedTab!.IsSelected);
        Assert.False(firstTab!.IsSelected);
    }

    [Fact]
    public void ClosingATab_SelectsANeighbor()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"));
        viewModel.NewTabCommand.Execute(null);
        viewModel.NewTabCommand.Execute(null);
        Assert.Equal(3, viewModel.Tabs.Count);
        var middleTab = viewModel.Tabs[1];
        viewModel.SelectTabCommand.Execute(middleTab);

        middleTab.CloseCommand.Execute(null);

        Assert.Equal(2, viewModel.Tabs.Count);
        Assert.DoesNotContain(middleTab, viewModel.Tabs);
        Assert.NotNull(viewModel.SelectedTab);
    }

    [Fact]
    public void ClosingTheLastTab_ResetsToOneFreshBlankTabInsteadOfZero()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"));
        var onlyTab = viewModel.Tabs[0];

        onlyTab.CloseCommand.Execute(null);

        Assert.Single(viewModel.Tabs);
        Assert.NotSame(onlyTab, viewModel.Tabs[0]);
        Assert.True(viewModel.SelectedTab!.IsSelected);
    }

    // SDA-03: Ctrl+W routes through the same close path a tab's own ✕ button uses.
    [Fact]
    public void CloseSelectedTabCommand_ClosesTheCurrentlySelectedTab()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"));
        viewModel.NewTabCommand.Execute(null);
        var selected = viewModel.SelectedTab;
        Assert.Equal(2, viewModel.Tabs.Count);

        viewModel.CloseSelectedTabCommand.Execute(null);

        Assert.Single(viewModel.Tabs);
        Assert.DoesNotContain(selected, viewModel.Tabs);
    }
}

public class BrowserViewModelTests
{
    // Distinct from BrowserTabViewModelTests' scheme/whitelist-rejection unit tests —
    // this one specifically exercises BrowserViewModel's own RequestWhitelistAsync
    // delegate talking to a genuinely unreachable ApiClient, container-level wiring the
    // per-tab tests above don't cover. BlockedUri is driven directly rather than via a
    // real classify call: against this same unreachable ApiClient, classify itself would
    // now correctly surface as an Error ("couldn't verify"), not a Blocked decision — see
    // ClassifyAsync_UnreachableServer_FailsClosedWithAnErrorDecision below, which covers
    // that path. This test is specifically about RequestWhitelistCommand's own
    // unreachable-server handling once a tab is already in a blocked state, however it
    // got there.
    [Fact]
    public async Task SDA04_RequestWhitelistSurfacesUnreachableServerAsAMessage()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"));
        var tab = viewModel.SelectedTab!;
        tab.BlockedUri = new Uri("https://not-whitelisted.example.com");

        await tab.RequestWhitelistCommand.ExecuteAsync(null);

        Assert.Equal("Could not reach the server. Check your connection and try again.", tab.WhitelistRequestMessage);
    }

    // SDA-03 classification policy engine (Work Item A2): with no whitelist loaded and no
    // reachable server, ClassifyAsync must fail closed (Error), not silently allow.
    [Fact]
    public async Task ClassifyAsync_UnreachableServer_FailsClosedWithAnErrorDecision()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"));

        var decision = await viewModel.ClassifyAsync(new Uri("https://example.com"));

        Assert.Equal(NavigationDecisionKind.Error, decision.Kind);
    }

    [Fact]
    public async Task SDA08_SaveClipRequiresTitleForNewNote()
    {
        // Whitelist is empty without a reachable server, so drive CurrentSource
        // directly to isolate the clip-save validation from the whitelist check.
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"));
        viewModel.SelectedTab!.CurrentSource = new Uri("https://example.com");
        viewModel.IsNewNote = true;
        viewModel.ClipNoteTitle = "   ";

        await viewModel.SaveClipCommand.ExecuteAsync(null);

        Assert.Equal("Enter a title for the new note.", viewModel.ClipErrorMessage);
    }

    [Fact]
    public async Task SDA08_SaveClipRequiresSelectingAnExistingNoteWhenAppending()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"));
        viewModel.SelectedTab!.CurrentSource = new Uri("https://example.com");
        viewModel.IsNewNote = false;
        viewModel.SelectedExistingNote = null;

        await viewModel.SaveClipCommand.ExecuteAsync(null);

        Assert.Equal("Choose a note to append to.", viewModel.ClipErrorMessage);
    }
}
