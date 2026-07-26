using System;
using CommunityToolkit.Mvvm.ComponentModel;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SEK-01: embeds SEK's CodeEditor for the student's Coding app, rather than the Student
// Desktop App implementing its own code editor/runner UI. This ViewModel just owns the
// bridge mount; the actual editing/running surface is SEK, hosted by CodeEditorView's
// NativeWebView and driven through CodeBridge. No persistence yet — a scratch editor.
public partial class CodeEditorViewModel : ViewModelBase
{
    private readonly Guid _userId;

    public CodeBridge Bridge { get; }

    // Toggled by CodeEditorView's code-behind once the WebView's NavigationCompleted
    // fires — the host bundle's JS needs to be loaded before __sekHostMount exists, so a
    // "Loading…" placeholder covers that window instead of showing a blank WebView.
    [ObservableProperty]
    private bool _isLoaded;

    public CodeEditorViewModel(ApiClient apiClient, Guid userId)
    {
        _userId = userId;
        Bridge = new CodeBridge(apiClient);
    }

    public void Mount() => _ = Bridge.MountAsync(_userId, canRun: true, canEdit: true);
}
