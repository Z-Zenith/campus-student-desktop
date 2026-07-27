using Avalonia.Controls;
using Avalonia.Interactivity;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Views;

public partial class BrowserView : UserControl
{
    public BrowserView()
    {
        InitializeComponent();
    }

    // Each open tab renders its own NativeWebView (see BrowserView.axaml's Tabs
    // ItemsControl) — there is no single named element to wire once anymore, so each
    // instance wires itself up to its own BrowserTabViewModel as it's realized.
    private void OnWebViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not NativeWebView webView || webView.DataContext is not BrowserTabViewModel tab)
        {
            return;
        }

        // SDA-08: the ViewModel stays UI-agnostic — it calls back into the actual
        // WebView only through these delegates, wired here rather than the ViewModel
        // holding a reference to an Avalonia control.
        tab.GetPageTitleAsync = () => webView.InvokeScript("document.title");
        tab.GetSelectedTextAsync = () => webView.InvokeScript("window.getSelection().toString()");
        tab.GoBackRequested = () => webView.GoBack();
        tab.GoForwardRequested = () => webView.GoForward();
        tab.ReloadRequested = () => webView.Refresh();
        tab.CanGoBack = () => webView.CanGoBack;
        tab.CanGoForward = () => webView.CanGoForward;
    }

    // SDA-03/SDA-04: the whitelist check must gate every navigation the WebView itself
    // initiates (link clicks inside the page, redirects), not just the ones the URL bar's
    // Navigate command triggers.
    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (sender is not NativeWebView webView || webView.DataContext is not BrowserTabViewModel tab)
        {
            return;
        }
        if (e.Request is null || !tab.IsWhitelisted(e.Request))
        {
            e.Cancel = true;
            return;
        }
        tab.IsLoading = true;
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (sender is not NativeWebView webView || webView.DataContext is not BrowserTabViewModel tab)
        {
            return;
        }
        tab.IsLoading = false;
        tab.RefreshNavigationState();
        tab.PageTitle = await webView.InvokeScript("document.title");
    }
}
