using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudentDesktop.ViewModels;

// A single interactive to-do row: checkbox (complete/incomplete), inline edit
// (title/due date/priority), and delete, backed by the student-owned Todo CRUD endpoints
// (SDA-14).
public partial class TodoItemViewModel : ViewModelBase
{
    private readonly Func<TodoItemViewModel, bool, Task> _onToggleComplete;
    private readonly Func<TodoItemViewModel, Task> _onDelete;
    private readonly Func<TodoItemViewModel, string, DateTime?, int, Task<bool>> _onEdit;
    private bool _suppressToggle;

    // Snapshot taken on BeginEdit so CancelEdit can revert without a round-trip.
    private string _titleBeforeEdit = "";
    private DateTime? _dueDateBeforeEdit;
    private int _priorityBeforeEdit;

    public Guid Id { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private DateTime? _dueDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverdue))]
    private int _priority;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverdue))]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isEditing;

    // Draft fields bound by the inline-edit row — kept separate from Title/DueDate/Priority
    // so a Cancel doesn't need to re-fetch or leave stray partial edits visible mid-type.
    [ObservableProperty]
    private string _editTitle = "";

    [ObservableProperty]
    private DateTime? _editDueDate;

    [ObservableProperty]
    private int _editPriority;

    public bool IsOverdue => DueDate.HasValue && DueDate.Value.Date < DateTime.Today && !IsCompleted;

    public TodoItemViewModel(Guid id, string title, DateTime? dueDate, int priority, bool completed,
        Func<TodoItemViewModel, bool, Task> onToggleComplete, Func<TodoItemViewModel, Task> onDelete,
        Func<TodoItemViewModel, string, DateTime?, int, Task<bool>> onEdit)
    {
        Id = id;
        _title = title;
        _dueDate = dueDate;
        _priority = priority;
        _isCompleted = completed;
        _onToggleComplete = onToggleComplete;
        _onDelete = onDelete;
        _onEdit = onEdit;
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
    private void BeginEdit()
    {
        _titleBeforeEdit = Title;
        _dueDateBeforeEdit = DueDate;
        _priorityBeforeEdit = Priority;
        EditTitle = Title;
        EditDueDate = DueDate;
        EditPriority = Priority;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task CommitEditAsync()
    {
        var title = EditTitle.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var ok = await _onEdit(this, title, EditDueDate, EditPriority);
        if (!ok)
        {
            return;
        }
        Title = title;
        DueDate = EditDueDate;
        Priority = EditPriority;
        IsEditing = false;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditTitle = _titleBeforeEdit;
        EditDueDate = _dueDateBeforeEdit;
        EditPriority = _priorityBeforeEdit;
        IsEditing = false;
    }

    [RelayCommand]
    private Task Delete() => _onDelete(this);
}
