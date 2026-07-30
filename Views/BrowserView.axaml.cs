using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Views;

public partial class BrowserView : UserControl
{
    public BrowserView()
    {
        InitializeComponent();
    }

    // Chrome-style keyboard shortcuts: Ctrl+T (new tab), Ctrl+W (close current tab),
    // Ctrl+L (focus + select-all the address bar, matching Chrome's own Ctrl+L).
    // Handled here rather than as XAML KeyBindings since Ctrl+L needs to call back
    // into a named control (AddressBar), not just execute a parameterless command.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || DataContext is not BrowserViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.T:
                viewModel.NewTabCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.W:
                viewModel.CloseSelectedTabCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.L:
                AddressBar.Focus();
                AddressBar.SelectAll();
                e.Handled = true;
                break;
        }
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

    // SDA-03/SDA-04: the classification gate must cover every navigation the WebView
    // itself initiates (link clicks inside the page, redirects), not just the ones the
    // URL bar's Navigate command triggers. The classify call is async, but this event
    // handler can't await mid-flight and then choose whether to cancel — WebView2/WKWebView/
    // WPE all expect e.Cancel decided synchronously within the event. So: cancel eagerly
    // for every in-page navigation, run the classify call in the background, and if it
    // resolves to Allowed, re-navigate programmatically by setting CurrentSource (bound to
    // NativeWebView.Source in BrowserView.axaml) rather than trying to un-cancel the
    // original navigation.
    private async void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (sender is not NativeWebView webView || webView.DataContext is not BrowserTabViewModel tab)
        {
            return;
        }

        var uri = e.Request;
        e.Cancel = true;
        if (uri is null)
        {
            return;
        }

        tab.IsCheckingNavigation = true;
        NavigationDecision decision;
        try
        {
            decision = await tab.ClassifyNavigationAsync(uri);
        }
        finally
        {
            tab.IsCheckingNavigation = false;
        }

        tab.ApplyDecision(uri, decision);
        if (decision.Kind == NavigationDecisionKind.Allowed)
        {
            tab.IsLoading = true;
        }
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
