using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SDA-17: lets a student submit feedback about a teacher they're actually taught by.
// SubjectName is shown for context only — the backend's teacher_feedback schema has no
// subject/course column, so feedback is attributable to the teacher, not a specific course.
public partial class TeacherFeedbackViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient;

    public ObservableCollection<MyTeacherDto> Teachers { get; } = [];

    [ObservableProperty]
    private MyTeacherDto? _selectedTeacher;

    // Neutral default, not the maximum — defaulting to 5 would bias submissions toward a
    // 5-star rating for anyone who doesn't deliberately move the control.
    [ObservableProperty]
    private int _rating = 3;

    [ObservableProperty]
    private string? _comments;

    // A 1-5 star row is a clearer rating affordance than a spinner — 5 fixed commands
    // (rather than one parameterized command) avoids CommandParameter type-coercion
    // pitfalls in Avalonia's compiled bindings for a plain int.
    [RelayCommand]
    private void SetRating1() => Rating = 1;

    [RelayCommand]
    private void SetRating2() => Rating = 2;

    [RelayCommand]
    private void SetRating3() => Rating = 3;

    [RelayCommand]
    private void SetRating4() => Rating = 4;

    [RelayCommand]
    private void SetRating5() => Rating = 5;

    // SubmitCommand's CanExecute is re-evaluated whenever this changes (see below) — the
    // XAML IsEnabled binding alone isn't a hard guard against a second click landing before
    // the UI re-renders, and a double-submit here means a real duplicate row in
    // teacher_feedback (unlike, say, a double password-change, which is naturally idempotent).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    public TeacherFeedbackViewModel(ApiClient apiClient)
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
            var teachers = await _apiClient.GetMyTeachersAsync();
            Teachers.Clear();
            foreach (var teacher in teachers)
            {
                Teachers.Add(teacher);
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

    private bool CanSubmit() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        if (SelectedTeacher is null)
        {
            ErrorMessage = "Choose a teacher first.";
            return;
        }

        var teacherName = SelectedTeacher.TeacherName;

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        try
        {
            await _apiClient.SubmitTeacherFeedbackAsync(SelectedTeacher.TeacherId, Rating, Comments);
            SuccessMessage = $"Feedback submitted for {teacherName}.";
            // Reset the form so a second click of the (now re-enabled) button submits a
            // deliberate new choice rather than silently repeating the same submission.
            SelectedTeacher = null;
            Rating = 3;
            Comments = null;
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
