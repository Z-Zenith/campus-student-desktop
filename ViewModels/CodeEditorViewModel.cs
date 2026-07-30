using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
// NativeWebView and driven through CodeBridge. Work Item C (SDA/SEK plan): the project
// list/detail reads below come from the local encrypted scratch store, not the backend —
// code projects here are scratch/working files that never sync to the cloud. _apiClient
// is still used for run/runPreview/terminal (genuine server-side execution) and for the
// one explicit Submit action (SubmitCurrentProjectAsync), which is the sole path that
// still reaches the backend.
public partial class CodeEditorViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly ILocalStore _localStore;
    private readonly Guid _userId;

    public CodeBridge Bridge { get; }

    public ObservableCollection<CodeProjectSummaryDto> Projects { get; } = [];

    // Set true only once Bridge.MountSucceeded fires (the host bundle's
    // window.__sekHostMount actually ran) — NavigationCompleted alone only means the
    // WebView's document loaded, not that the editor mounted, so a "Loading…"
    // placeholder covers the gap instead of ever showing a blank, unmounted WebView.
    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private CodeProjectSummaryDto? _selectedProject;

    [ObservableProperty]
    private SubmissionDto? _lastSubmission;

    // C3: matches AssignmentsViewModel's own manual-entry pattern (AssignmentId as a
    // parsed text field) rather than a dropdown — CodeEditorViewModel isn't handed a
    // reference to AssignmentsViewModel's loaded assignment list today, and adding one
    // just for this would be a bigger cross-ViewModel wiring change than this action needs.
    [ObservableProperty]
    private string _submitAssignmentId = "";

    public CodeEditorViewModel(ApiClient apiClient, Guid userId, ILocalStore? localStore = null)
    {
        _apiClient = apiClient;
        _userId = userId;
        _localStore = localStore ?? new LazyLocalStore();
        Bridge = new CodeBridge(apiClient, _localStore);
        Bridge.ProjectChanged += () => _ = LoadProjectsAsync();
        // IsLoaded now means "the editor actually mounted", not just "the WebView
        // navigated" — a navigated-but-unmounted WebView is still a blank pane.
        Bridge.MountSucceeded += () => IsLoaded = true;
        Bridge.MountFailed += message => ErrorMessage = $"Failed to load the code editor: {message}";
        _ = LoadProjectsAsync();
    }

    [RelayCommand]
    private async Task LoadProjectsAsync()
    {
        try
        {
            var projects = await _localStore.ListCodeProjectsAsync();
            Projects.Clear();
            foreach (var project in projects)
            {
                Projects.Add(project);
            }
        }
        catch (LocalStoreUnavailableException ex)
        {
            ErrorMessage = ex.Message;
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
            var full = await _localStore.GetCodeProjectAsync(summary.Id);
            await Bridge.MountAsync(_userId, full, canRun: true, canEdit: true);
        }
        catch (LocalStoreUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // C3 (SDA/SEK plan): the one explicit, one-time upload from local scratch storage to
    // the backend — everything else in this ViewModel reads/writes ILocalStore only. Reuses
    // the existing, unmodified SDA-10 submissions endpoint (ApiClient.SubmitAssignmentAsync);
    // no backend contract change. That endpoint's ContentUrl is expected to be a real,
    // externally-fetchable URL (AssignmentsController hands it to Copyleaks/AI autograde,
    // which fetch it themselves) — this codebase has no file/blob upload endpoint to turn
    // local files into one, matching how a student would already need to host code
    // externally (e.g. a Gist/repo link) to use SDA-10's existing manual-entry form today.
    // Encoding the project as a data: URI keeps this call site honest about what it can
    // actually guarantee (the exact bytes reach the submission record) without inventing
    // new backend infrastructure; it's a real, documented limitation, not silently pretended
    // away — a data: URI won't be fetchable by Copyleaks/autograde, so a submission made
    // this way should be treated as "recorded", not "scanned", until a real upload endpoint
    // exists.
    [RelayCommand]
    private async Task SubmitCurrentProjectAsync()
    {
        if (SelectedProject is null)
        {
            ErrorMessage = "Open a project before submitting.";
            return;
        }
        if (!Guid.TryParse(SubmitAssignmentId.Trim(), out var assignmentId))
        {
            ErrorMessage = "Enter a valid assignment ID.";
            return;
        }
        ErrorMessage = null;
        try
        {
            var project = await _localStore.GetCodeProjectAsync(SelectedProject.Id);
            if (project is null)
            {
                ErrorMessage = "This project could not be found in local storage.";
                return;
            }
            var contentUrl = ToDataUrl(project);
            LastSubmission = await _apiClient.SubmitAssignmentAsync(assignmentId, contentUrl, "Code");
        }
        catch (LocalStoreUnavailableException ex)
        {
            ErrorMessage = ex.Message;
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

    private static string ToDataUrl(CodeProjectDto project)
    {
        var json = JsonSerializer.Serialize(project);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return $"data:application/json;base64,{base64}";
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
