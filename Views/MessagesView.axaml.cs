using System;
using System.IO;
using Avalonia.Controls;
using StudentDesktop.Services;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Views;

public partial class MessagesView : UserControl
{
    // SDA-24: dist/host/** from packages/direct-messaging is copied here at build time
    // (see StudentDesktop.csproj) by `npm run build:host` in that package.
    private const string HostIndexRelativePath = "DmsHost/index.html";

    public MessagesView()
    {
        InitializeComponent();
        MessagesWebView.WebMessageReceived += OnWebMessageReceived;
        MessagesWebView.NavigationCompleted += (_, _) =>
        {
            // #11 (SDA-21): re-inject the clipboard isolation script after every navigation —
            // see WebViewClipboardBridge for the full rationale.
            _ = MessagesWebView.InvokeScript(WebViewClipboardBridge.InjectScript);
            if (DataContext is MessagesViewModel vm)
            {
                vm.IsLoaded = true;
            }
        };
        DataContextChanged += (_, _) => WireViewModel();
        MessagesWebView.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, HostIndexRelativePath)));
    }

    private void WireViewModel()
    {
        if (DataContext is not MessagesViewModel vm)
        {
            return;
        }

        vm.Bridge.InvokeScript = script => MessagesWebView.InvokeScript(script);
        _ = vm.MountAsync();
    }

    private async void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (e.Body is not { } body)
        {
            return;
        }

        // #11 (SDA-21): the clipboard bridge's own copy/cut/paste signals are handled here and
        // never forwarded to DmsBridge — they're a distinct, app-generated protocol, not a DMS
        // host request.
        if (await WebViewClipboardBridge.TryHandleAsync(body, AppClipboardService.Instance, MessagesWebView.InvokeScript))
        {
            return;
        }

        if (DataContext is MessagesViewModel vm)
        {
            _ = vm.Bridge.HandleMessageAsync(body);
        }
    }
}
