using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Models;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// SDA-14: college-wide events, registered events, personal to-dos, and custom entries in a
// single calendar. Class sessions (from the student's timetable) are overlaid on the same
// week grid as events, Google-Calendar-style, per the desktop app's design direction — the
// other three kinds are distinguished as separate labeled lists below the grid so all four
// stay visually distinguishable even though only two are time-slot-shaped. To-dos and
// custom entries are student-owned and fully interactive (add/complete/delete); college
// events and class sessions are read-only here (they're created/managed elsewhere).
public partial class CalendarViewModel : ViewModelBase
{
    private static readonly int FirstHour = 7;
    private static readonly int LastHour = 20;
    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private readonly ApiClient _apiClient;

    public ObservableCollection<CalendarCellViewModel> GridCells { get; } = [];
    public ObservableCollection<TodoItemViewModel> Todos { get; } = [];
    public ObservableCollection<CustomEntryItemViewModel> CustomEntries { get; } = [];
    public ObservableCollection<CalendarListItemViewModel> OtherEvents { get; } = [];
    public ObservableCollection<CalendarListItemViewModel> OtherClasses { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTodoCommand))]
    private string _newTodoTitle = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCustomEntryCommand))]
    private string _newCustomEntryTitle = "";

    public CalendarViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
        BuildGridSkeleton();
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var response = await _apiClient.GetMyCalendarAsync();
            PlaceItems(response.Items);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAddTodo() => !string.IsNullOrWhiteSpace(NewTodoTitle);

    [RelayCommand(CanExecute = nameof(CanAddTodo))]
    private async Task AddTodoAsync()
    {
        ErrorMessage = null;
        try
        {
            await _apiClient.CreateTodoAsync(NewTodoTitle.Trim(), dueDate: null);
            NewTodoTitle = "";
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task OnToggleTodoCompleteAsync(TodoItemViewModel todo, bool completed)
    {
        try
        {
            await _apiClient.SetTodoCompleteAsync(todo.Id, completed);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            todo.SetCompletedWithoutNotifying(!completed);
        }
    }

    private async Task OnDeleteTodoAsync(TodoItemViewModel todo)
    {
        try
        {
            await _apiClient.DeleteTodoAsync(todo.Id);
            Todos.Remove(todo);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private bool CanAddCustomEntry() => !string.IsNullOrWhiteSpace(NewCustomEntryTitle);

    [RelayCommand(CanExecute = nameof(CanAddCustomEntry))]
    private async Task AddCustomEntryAsync()
    {
        ErrorMessage = null;
        try
        {
            await _apiClient.CreateCustomCalendarEntryAsync(NewCustomEntryTitle.Trim(), DateOnly.FromDateTime(DateTime.Today));
            NewCustomEntryTitle = "";
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task OnDeleteCustomEntryAsync(CustomEntryItemViewModel entry)
    {
        try
        {
            await _apiClient.DeleteCustomCalendarEntryAsync(entry.Id);
            CustomEntries.Remove(entry);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void BuildGridSkeleton()
    {
        GridCells.Add(new CalendarCellViewModel(0, 0, "header"));

        var monday = ThisWeekMonday();
        for (var day = 0; day < 7; day++)
        {
            var date = monday.AddDays(day);
            GridCells.Add(new CalendarCellViewModel(0, day + 1, "header", DayNames[day], date.ToString("MMM d")));
        }

        for (var hour = FirstHour; hour <= LastHour; hour++)
        {
            var row = hour - FirstHour + 1;
            GridCells.Add(new CalendarCellViewModel(row, 0, "hour", $"{hour}:00"));
        }
    }

    private void PlaceItems(List<CalendarItemDto> items)
    {
        GridCells.Where(c => c.Kind is "class_session" or "college_event-grid").ToList()
            .ForEach(c => GridCells.Remove(c));
        Todos.Clear();
        CustomEntries.Clear();
        OtherEvents.Clear();
        OtherClasses.Clear();

        var monday = ThisWeekMonday();
        var weekEnd = monday.AddDays(7);

        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case "todo":
                    Todos.Add(new TodoItemViewModel(item.Id, item.Title, item.Start,
                        item.Extra == "completed=true", OnToggleTodoCompleteAsync, OnDeleteTodoAsync));
                    break;
                case "custom_entry":
                    CustomEntries.Add(new CustomEntryItemViewModel(item.Id, item.Title,
                        DateOnly.FromDateTime(item.Start), OnDeleteCustomEntryAsync));
                    break;
                case "college_event":
                    var registered = item.Extra == "registered=true";
                    if (item.Start >= monday && item.Start < weekEnd && item.Start.Hour >= FirstHour && item.Start.Hour <= LastHour)
                    {
                        var col = (item.Start.Date - monday.Date).Days + 1;
                        var row = item.Start.Hour - FirstHour + 1;
                        GridCells.Add(new CalendarCellViewModel(row, col, "college_event-grid", item.Title,
                            registered ? "Registered" : null));
                    }
                    else
                    {
                        OtherEvents.Add(new CalendarListItemViewModel(item.Title, item.Start, registered ? "Registered" : null));
                    }
                    break;
                case "class_session":
                    var classCol = (item.Start.Date - monday.Date).Days + 1;
                    var classRow = item.Start.Hour - FirstHour + 1;
                    if (classCol is >= 1 and <= 7 && classRow >= 1 && item.Start.Hour <= LastHour)
                    {
                        GridCells.Add(new CalendarCellViewModel(classRow, classCol, "class_session", item.Title, item.Extra));
                    }
                    else
                    {
                        // Outside the 7am-8pm grid (e.g. a manually-edited slot moved to an
                        // early/late time) — still show it rather than dropping it silently.
                        OtherClasses.Add(new CalendarListItemViewModel(item.Title, item.Start, item.Extra));
                    }
                    break;
            }
        }
    }

    private static DateTime ThisWeekMonday()
    {
        var today = DateTime.Now.Date;
        var offset = today.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)today.DayOfWeek - 1;
        return today.AddDays(-offset);
    }
}
