using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace StudentDesktop.ViewModels;

// A single interactive custom-calendar-entry row: title/date + delete (SDA-14).
public partial class CustomEntryItemViewModel(Guid id, string title, DateOnly entryDate, Func<CustomEntryItemViewModel, Task> onDelete) : ViewModelBase
{
    public Guid Id { get; } = id;
    public string Title { get; } = title;
    public DateOnly EntryDate { get; } = entryDate;

    [RelayCommand]
    private Task Delete() => onDelete(this);
}
