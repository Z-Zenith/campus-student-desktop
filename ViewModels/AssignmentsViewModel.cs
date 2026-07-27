using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SDA-10: submit an assignment in the format matching its type (code, quiz, essay, file
// upload) and record a submission timestamp. Late/format-mismatch enforcement lives
// server-side (AssignmentsController.Submit) — this just forwards and surfaces the result.
// The tile grid (Assignments list) selects an assignment, which prefills the submit form
// below instead of requiring the student to type a GUID.
public partial class AssignmentsViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;

    public ObservableCollection<AssignmentSummaryDto> Assignments { get; } = [];

    [ObservableProperty]
    private AssignmentSummaryDto? _selectedAssignment;

    [ObservableProperty]
    private string _assignmentId = "";

    [ObservableProperty]
    private string _submissionFormat = "Code";

    [ObservableProperty]
    private string _contentUrl = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private SubmissionDto? _lastSubmission;

    public AssignmentsViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var assignments = await _apiClient.GetMyAssignmentsAsync();
            Assignments.Clear();
            foreach (var assignment in assignments)
            {
                Assignments.Add(assignment);
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
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAssignment(AssignmentSummaryDto assignment) => SelectedAssignment = assignment;

    partial void OnSelectedAssignmentChanged(AssignmentSummaryDto? value)
    {
        if (value is null)
        {
            return;
        }
        AssignmentId = value.Id.ToString();
        SubmissionFormat = value.Type;
        LastSubmission = null;
        ErrorMessage = null;
    }

    private bool CanSubmit() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;
        LastSubmission = null;

        if (!Guid.TryParse(AssignmentId.Trim(), out var assignmentId))
        {
            ErrorMessage = "Enter a valid assignment ID.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ContentUrl))
        {
            ErrorMessage = "Enter your submission content.";
            return;
        }

        IsBusy = true;
        try
        {
            LastSubmission = await _apiClient.SubmitAssignmentAsync(assignmentId, ContentUrl.Trim(), SubmissionFormat);
            ContentUrl = "";
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Could not reach the server. Check your connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
