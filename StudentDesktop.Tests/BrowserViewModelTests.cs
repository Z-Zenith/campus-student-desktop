using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Tests;

// SDA-03/04's scheme/whitelist-rejection scenarios now live on BrowserTabViewModel
// (the per-tab unit), constructed directly with fake isWhitelisted/requestWhitelistAsync
// delegates — no ApiClient/HTTP dependency needed for these anymore, unlike before the
// Chrome-style multi-tab refactor. BrowserViewModelTests below covers the container:
// initial-tab creation, tab management, and Clip-to-Notes (which stayed container-level).
public class BrowserTabViewModelTests
{
    private static BrowserTabViewModel NewTab(Func<Uri, bool>? isWhitelisted = null, Func<Uri, Task<string>>? requestWhitelistAsync = null) =>
        new(isWhitelisted ?? (_ => false), requestWhitelistAsync ?? (_ => Task.FromResult("")));

    [Fact]
    public void SDA03_NavigateRejectsNonHttpUrl()
    {
        var tab = NewTab(isWhitelisted: _ => true);
        tab.UrlInput = "ftp://example.com";

        tab.NavigateCommand.Execute(null);

        Assert.Null(tab.CurrentSource);
        Assert.Equal("Enter a valid http:// or https:// address.", tab.ErrorMessage);
    }

    [Fact]
    public void SDA03_NavigateRejectsHostNotOnWhitelist()
    {
        var tab = NewTab(isWhitelisted: _ => false);
        tab.UrlInput = "https://not-whitelisted.example.com";

        tab.NavigateCommand.Execute(null);

        Assert.Null(tab.CurrentSource);
        Assert.Contains("not on the whitelist", tab.ErrorMessage);
    }

    [Fact]
    public void SDA04_NavigateBlockedSiteExposesItForRequest()
    {
        var tab = NewTab(isWhitelisted: _ => false);
        tab.UrlInput = "https://not-whitelisted.example.com";

        tab.NavigateCommand.Execute(null);

        Assert.Equal(new Uri("https://not-whitelisted.example.com"), tab.BlockedUri);
        Assert.True(tab.RequestWhitelistCommand.CanExecute(null));
    }

    [Fact]
    public void SDA04_NavigateToInvalidUrlDoesNotOfferAWhitelistRequest()
    {
        var tab = NewTab(isWhitelisted: _ => true);
        tab.UrlInput = "ftp://example.com";

        tab.NavigateCommand.Execute(null);

        Assert.Null(tab.BlockedUri);
        Assert.False(tab.RequestWhitelistCommand.CanExecute(null));
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
}

public class BrowserViewModelTests
{
    // Distinct from BrowserTabViewModelTests' scheme/whitelist-rejection unit tests —
    // this one specifically exercises BrowserViewModel's own RequestWhitelistAsync
    // delegate talking to a genuinely unreachable ApiClient, container-level wiring the
    // per-tab tests above don't cover.
    [Fact]
    public async Task SDA04_RequestWhitelistSurfacesUnreachableServerAsAMessage()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"));
        var tab = viewModel.SelectedTab!;
        tab.UrlInput = "https://not-whitelisted.example.com";
        tab.NavigateCommand.Execute(null);

        await tab.RequestWhitelistCommand.ExecuteAsync(null);

        Assert.Equal("Could not reach the server. Check your connection and try again.", tab.WhitelistRequestMessage);
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
