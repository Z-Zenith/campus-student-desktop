using System;
using System.IO;
using Avalonia.Controls;
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

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (DataContext is NotesViewModel vm && e.Body is { } body)
        {
            _ = vm.Bridge.HandleMessageAsync(body);
        }
    }
}
