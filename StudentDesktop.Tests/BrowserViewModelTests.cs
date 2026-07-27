using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Tests;

public class BrowserViewModelTests
{
    [Fact]
    public void SDA03_NavigateRejectsNonHttpUrl()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0")) { UrlInput = "ftp://example.com" };

        viewModel.NavigateCommand.Execute(null);

        Assert.Null(viewModel.CurrentSource);
        Assert.Equal("Enter a valid http:// or https:// address.", viewModel.ErrorMessage);
    }

    [Fact]
    public void SDA03_NavigateRejectsHostNotOnWhitelist()
    {
        // No reachable server, so the whitelist never loads and stays empty —
        // every host must be rejected rather than fail open.
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0")) { UrlInput = "https://not-whitelisted.example.com" };

        viewModel.NavigateCommand.Execute(null);

        Assert.Null(viewModel.CurrentSource);
        Assert.Contains("not on the whitelist", viewModel.ErrorMessage);
    }

    [Fact]
    public void SDA04_NavigateBlockedSiteExposesItForRequest()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0")) { UrlInput = "https://not-whitelisted.example.com" };

        viewModel.NavigateCommand.Execute(null);

        Assert.Equal(new Uri("https://not-whitelisted.example.com"), viewModel.BlockedUri);
        Assert.True(viewModel.RequestWhitelistCommand.CanExecute(null));
    }

    [Fact]
    public void SDA04_NavigateToInvalidUrlDoesNotOfferAWhitelistRequest()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0")) { UrlInput = "ftp://example.com" };

        viewModel.NavigateCommand.Execute(null);

        Assert.Null(viewModel.BlockedUri);
        Assert.False(viewModel.RequestWhitelistCommand.CanExecute(null));
    }

    [Fact]
    public async Task SDA04_RequestWhitelistSurfacesUnreachableServerAsAMessage()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0")) { UrlInput = "https://not-whitelisted.example.com" };
        viewModel.NavigateCommand.Execute(null);

        await viewModel.RequestWhitelistCommand.ExecuteAsync(null);

        Assert.Equal("Could not reach the server. Check your connection and try again.", viewModel.WhitelistRequestMessage);
    }

    [Fact]
    public async Task SDA08_SaveClipRequiresTitleForNewNote()
    {
        // Whitelist is empty without a reachable server, so drive CurrentSource
        // directly to isolate the clip-save validation from the whitelist check.
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"))
        {
            CurrentSource = new Uri("https://example.com"),
            IsNewNote = true,
            ClipNoteTitle = "   ",
        };

        await viewModel.SaveClipCommand.ExecuteAsync(null);

        Assert.Equal("Enter a title for the new note.", viewModel.ClipErrorMessage);
    }

    [Fact]
    public async Task SDA08_SaveClipRequiresSelectingAnExistingNoteWhenAppending()
    {
        var viewModel = new BrowserViewModel(new ApiClient("http://localhost:0"))
        {
            CurrentSource = new Uri("https://example.com"),
            IsNewNote = false,
            SelectedExistingNote = null,
        };

        await viewModel.SaveClipCommand.ExecuteAsync(null);

        Assert.Equal("Choose a note to append to.", viewModel.ClipErrorMessage);
    }
}
