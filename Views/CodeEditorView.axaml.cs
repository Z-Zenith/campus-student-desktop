using System;
using System.IO;
using Avalonia.Controls;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Views;

public partial class CodeEditorView : UserControl
{
    // SEK-01: dist/host-code/** from shared-editor-kit is copied here at build time (see
    // StudentDesktop.csproj) by the same `npm run build:host` that builds SekHost.
    private const string HostIndexRelativePath = "CodeHost/index.html";

    // DataContextChanged and NavigationCompleted are two independent, unordered async
    // events — whichever fires second is the one that actually mounts. Mounting only from
    // NavigationCompleted risked a silent no-op if it happened to fire before DataContext
    // (and therefore Bridge.InvokeScript) was wired, with nothing ever retrying it.
    private bool _isNavigated;
    private bool _isViewModelWired;

    public CodeEditorView()
    {
        InitializeComponent();
        EditorWebView.WebMessageReceived += OnWebMessageReceived;
        EditorWebView.NavigationCompleted += OnNavigationCompleted;
        DataContextChanged += (_, _) => WireViewModel();
        EditorWebView.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, HostIndexRelativePath)));
    }

    private void WireViewModel()
    {
        if (DataContext is not CodeEditorViewModel vm)
        {
            return;
        }

        vm.Bridge.InvokeScript = script => EditorWebView.InvokeScript(script);
        _isViewModelWired = true;
        MountIfReady(vm);
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (DataContext is CodeEditorViewModel vm && e.Body is { } body)
        {
            _ = vm.Bridge.HandleMessageAsync(body);
        }
    }

    // The host bundle's JS (and __sekHostMount) only exists once the page has actually
    // loaded — mounting before that would silently no-op, so this is the earliest safe
    // point to mount. Does NOT clear the "Loading…" placeholder itself anymore — that
    // only means the WebView's document loaded, not that the editor actually mounted
    // (see CodeBridge's MountSucceeded/MountFailed events, wired to IsLoaded/ErrorMessage
    // in CodeEditorViewModel's constructor) — a WebView that navigated but never mounted
    // is still just a blank pane, which is exactly the bug this used to hide.
    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _isNavigated = true;
        if (DataContext is CodeEditorViewModel vm)
        {
            MountIfReady(vm);
        }
    }

    private void MountIfReady(CodeEditorViewModel vm)
    {
        if (_isNavigated && _isViewModelWired)
        {
            vm.Mount();
        }
    }
}
