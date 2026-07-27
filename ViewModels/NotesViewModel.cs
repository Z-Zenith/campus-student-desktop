using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SDA-19: embeds SEK-03's NotesEditor for all note-taking, rather than the Student
// Desktop App implementing its own note editor UI. This ViewModel owns the note list and
// which note is being edited; the actual editing surface is SEK, hosted by NotesView's
// NativeWebView and driven through SekBridge.
public partial class NotesViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;
    private readonly Guid _userId;

    public SekBridge Bridge { get; }

    public ObservableCollection<NoteSummaryDto> Notes { get; } = [];

    // Toggled by NotesView's code-behind once the WebView's NavigationCompleted fires.
    // Unlike CodeEditorViewModel, Notes doesn't auto-mount an editor on open (no note is
    // selected yet), so "loaded" here means "the WebView shell is ready," not "an editor
    // is mounted" — a blank pane with no note open is the correct resting state. Mount
    // failures (once the student does pick/create a note) surface via ErrorMessage
    // instead, from Bridge.MountFailed below.
    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private NoteSummaryDto? _selectedNote;

    public NotesViewModel(ApiClient apiClient, Guid userId)
    {
        _apiClient = apiClient;
        _userId = userId;
        Bridge = new SekBridge(apiClient);
        Bridge.NoteChanged += () => _ = LoadNotesAsync();
        Bridge.MountFailed += message => ErrorMessage = $"Failed to load the note editor: {message}";
        _ = LoadNotesAsync();
    }

    [RelayCommand]
    private async Task LoadNotesAsync()
    {
        try
        {
            var notes = await _apiClient.GetMyNotesAsync();
            Notes.Clear();
            foreach (var note in notes)
            {
                Notes.Add(note);
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
    private async Task NewNoteAsync()
    {
        SelectedNote = null;
        await Bridge.MountNotesEditorAsync(_userId, currentNote: null, canEdit: true);
    }

    partial void OnSelectedNoteChanged(NoteSummaryDto? value) => _ = MountSelectedNoteAsync(value);

    private async Task MountSelectedNoteAsync(NoteSummaryDto? summary)
    {
        if (summary is null)
        {
            return;
        }
        try
        {
            var full = await _apiClient.GetNoteAsync(summary.Id);
            await Bridge.MountNotesEditorAsync(_userId, full, canEdit: true);
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
}
