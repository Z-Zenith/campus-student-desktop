using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudentDesktop.ViewModels;

// A single interactive to-do row: checkbox (complete/incomplete) + delete, backed by the
// student-owned Todo CRUD endpoints (SDA-14).
public partial class TodoItemViewModel : ViewModelBase
{
    private readonly Func<TodoItemViewModel, bool, Task> _onToggleComplete;
    private readonly Func<TodoItemViewModel, Task> _onDelete;
    private bool _suppressToggle;

    public Guid Id { get; }
    public string Title { get; }
    public DateTime? DueDate { get; }

    [ObservableProperty]
    private bool _isCompleted;

    public TodoItemViewModel(Guid id, string title, DateTime? dueDate, bool completed,
        Func<TodoItemViewModel, bool, Task> onToggleComplete, Func<TodoItemViewModel, Task> onDelete)
    {
        Id = id;
        Title = title;
        DueDate = dueDate;
        _isCompleted = completed;
        _onToggleComplete = onToggleComplete;
        _onDelete = onDelete;
    }

    partial void OnIsCompletedChanged(bool value)
    {
        if (_suppressToggle)
        {
            return;
        }
        _ = _onToggleComplete(this, value);
    }

    // Lets the parent VM revert the checkbox on a failed API call without re-triggering
    // another toggle round-trip.
    public void SetCompletedWithoutNotifying(bool value)
    {
        _suppressToggle = true;
        IsCompleted = value;
        _suppressToggle = false;
    }

    [RelayCommand]
    private Task Delete() => _onDelete(this);
}
