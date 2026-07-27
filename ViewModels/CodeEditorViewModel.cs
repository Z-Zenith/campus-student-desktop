using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SEK-01: embeds SEK's CodeEditor for the student's Coding app, rather than the Student
// Desktop App implementing its own code editor/runner UI. This ViewModel owns the project
// list and which project is open, mirroring NotesViewModel's list/select/new pattern
// exactly; the actual editing/running surface is SEK, hosted by CodeEditorView's
// NativeWebView and driven through CodeBridge.
public partial class CodeEditorViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly Guid _userId;

    public CodeBridge Bridge { get; }

    public ObservableCollection<CodeProjectSummaryDto> Projects { get; } = [];

    // Toggled by CodeEditorView's code-behind once the WebView's NavigationCompleted
    // fires — the host bundle's JS needs to be loaded before __sekHostMount exists, so a
    // "Loading…" placeholder covers that window instead of showing a blank WebView.
    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private CodeProjectSummaryDto? _selectedProject;

    public CodeEditorViewModel(ApiClient apiClient, Guid userId)
    {
        _apiClient = apiClient;
        _userId = userId;
        Bridge = new CodeBridge(apiClient);
        Bridge.ProjectChanged += () => _ = LoadProjectsAsync();
        _ = LoadProjectsAsync();
    }

    [RelayCommand]
    private async Task LoadProjectsAsync()
    {
        try
        {
            var projects = await _apiClient.GetMyCodeProjectsAsync();
            Projects.Clear();
            foreach (var project in projects)
            {
                Projects.Add(project);
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        SelectedProject = null;
        await Bridge.MountAsync(_userId, currentProject: null, canRun: true, canEdit: true);
    }

    partial void OnSelectedProjectChanged(CodeProjectSummaryDto? value) => _ = MountSelectedProjectAsync(value);

    private async Task MountSelectedProjectAsync(CodeProjectSummaryDto? summary)
    {
        if (summary is null)
        {
            return;
        }
        try
        {
            var full = await _apiClient.GetCodeProjectAsync(summary.Id);
            await Bridge.MountAsync(_userId, full, canRun: true, canEdit: true);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
    }

    // Called once at first mount (before any project is selected from the sidebar) so the
    // Coding tab opens on a fresh, unsaved project instead of staying blank until the
    // student picks one — CodeEditorView's code-behind calls this from MountIfReady.
    public void Mount()
    {
        if (SelectedProject is null)
        {
            _ = Bridge.MountAsync(_userId, currentProject: null, canRun: true, canEdit: true);
        }
    }
}
