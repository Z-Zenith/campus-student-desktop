using System;
using System.IO;
using Avalonia.Controls;
using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Views;

public partial class NotesView : UserControl
{
    // SDA-19: dist/host/** from packages/shared-editor-kit is copied here at build time
    // (see StudentDesktop.csproj) by `npm run build:host` in that package.
    private const string HostIndexRelativePath = "SekHost/index.html";

    public NotesView()
    {
        InitializeComponent();
        EditorWebView.WebMessageReceived += OnWebMessageReceived;
        EditorWebView.NavigationCompleted += (_, _) =>
        {
            // #11 (SDA-21): re-inject the clipboard isolation script after every navigation —
            // see WebViewClipboardBridge for the full rationale.
            _ = EditorWebView.InvokeScript(WebViewClipboardBridge.InjectScript);
            if (DataContext is NotesViewModel vm)
            {
                vm.IsLoaded = true;
            }
        };
        DataContextChanged += (_, _) => WireViewModel();
        EditorWebView.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, HostIndexRelativePath)));
    }

    private void WireViewModel()
    {
        if (DataContext is not NotesViewModel vm)
        {
            return;
        }

        vm.Bridge.InvokeScript = script => EditorWebView.InvokeScript(script);
    }

    private async void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (e.Body is not { } body)
        {
            return;
        }

        // #11 (SDA-21): the clipboard bridge's own copy/cut/paste signals are handled here and
        // never forwarded to SekBridge — they're a distinct, app-generated protocol, not a SEK
        // host request.
        if (await WebViewClipboardBridge.TryHandleAsync(body, AppClipboardService.Instance, EditorWebView.InvokeScript))
        {
            return;
        }

        if (DataContext is NotesViewModel vm)
        {
            _ = vm.Bridge.HandleMessageAsync(body);
        }
    }
}
