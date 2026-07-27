using System;
using System.IO;
using Avalonia.Controls;
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

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (DataContext is MessagesViewModel vm && e.Body is { } body)
        {
            _ = vm.Bridge.HandleMessageAsync(body);
        }
    }
}
