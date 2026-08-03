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
    private List<CalendarItemDto> _lastLoadedItems = [];

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
    private DateTime? _newTodoDueDate;

    [ObservableProperty]
    private int _newTodoPriority;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCustomEntryCommand))]
    private string _newCustomEntryTitle = "";

    // The Monday of the week currently displayed in the grid — defaults to this week.
    // Navigable via PreviousWeek/NextWeek/GoToToday; class sessions are only ever "this
    // week" server-side (ThisWeeksClassSessionsAsync has no date-range parameter), so the
    // grid/list only shows them when this equals the real current week (see IsCurrentWeek).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentWeek))]
    [NotifyPropertyChangedFor(nameof(WeekRangeLabel))]
    private DateTime _viewedMonday = ThisWeekMonday();

    public bool IsCurrentWeek => ViewedMonday == ThisWeekMonday();

    public string WeekRangeLabel => $"{ViewedMonday:MMM d} – {ViewedMonday.AddDays(6):MMM d}";

    public CalendarViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
        BuildGridSkeleton();
        _ = LoadAsync();
        _ = LoadTodosAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var response = await _apiClient.GetMyCalendarAsync();
            _lastLoadedItems = response.Items;
            PlaceItems(_lastLoadedItems);
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

    [RelayCommand]
    private async Task LoadTodosAsync()
    {
        ErrorMessage = null;
        try
        {
            var todos = await _apiClient.GetMyTodosAsync();
            Todos.Clear();
            foreach (var todo in todos)
            {
                Todos.Add(new TodoItemViewModel(todo.Id, todo.Title, todo.DueDate, todo.Priority, todo.Completed,
                    OnToggleTodoCompleteAsync, OnDeleteTodoAsync, OnEditTodoAsync));
            }
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void PreviousWeek()
    {
        ViewedMonday = ViewedMonday.AddDays(-7);
        RebuildGridForViewedWeek();
    }

    [RelayCommand]
    private void NextWeek()
    {
        ViewedMonday = ViewedMonday.AddDays(7);
        RebuildGridForViewedWeek();
    }

    [RelayCommand]
    private void GoToToday()
    {
        ViewedMonday = ThisWeekMonday();
        RebuildGridForViewedWeek();
    }

    private void RebuildGridForViewedWeek()
    {
        GridCells.Clear();
        BuildGridSkeleton();
        PlaceItems(_lastLoadedItems);
    }

    private bool CanAddTodo() => !string.IsNullOrWhiteSpace(NewTodoTitle);

    [RelayCommand(CanExecute = nameof(CanAddTodo))]
    private async Task AddTodoAsync()
    {
        ErrorMessage = null;
        try
        {
            await _apiClient.CreateTodoAsync(NewTodoTitle.Trim(), NewTodoDueDate, NewTodoPriority);
            NewTodoTitle = "";
            NewTodoDueDate = null;
            NewTodoPriority = 0;
            await LoadTodosAsync();
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

    private async Task<bool> OnEditTodoAsync(TodoItemViewModel todo, string title, DateTime? dueDate, int priority)
    {
        try
        {
            await _apiClient.UpdateTodoAsync(todo.Id, title, dueDate, priority);
            return true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return false;
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

        for (var day = 0; day < 7; day++)
        {
            var date = ViewedMonday.AddDays(day);
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
        CustomEntries.Clear();
        OtherEvents.Clear();
        OtherClasses.Clear();

        var monday = ViewedMonday;
        var weekEnd = monday.AddDays(7);

        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case "todo":
                    // Todos are sourced from GetMyTodosAsync (LoadTodosAsync), not this
                    // dated-only feed — calendar/mine omits undated todos by design (#159),
                    // which is exactly the bug the standalone Todos list must not inherit.
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
                    // The backend always returns "this week"'s class sessions, regardless of
                    // which week the client is viewing (no date-range param exists yet) —
                    // showing them under a different week's grid would misrepresent the
                    // schedule, so they're only placed while IsCurrentWeek.
                    if (!IsCurrentWeek)
                    {
                        break;
                    }
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

    // #7: item.Start/.End (CalendarItemDto) are server-sourced UTC timestamps (see
    // Models/ApiModels.cs), so the week-grid boundary computed here has to be UTC-based too
    // — DateTime.Now.Date (local wall-clock) would, for any positive-UTC-offset timezone,
    // shift a late-local-evening session into the wrong UTC day/week column near week/day
    // boundaries. Matches every other comparison against server-sourced times in this
    // codebase (see ClassLockService.Evaluate).
    private static DateTime ThisWeekMonday()
    {
        var today = DateTime.UtcNow.Date;
        var offset = today.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)today.DayOfWeek - 1;
        return today.AddDays(-offset);
    }
}
