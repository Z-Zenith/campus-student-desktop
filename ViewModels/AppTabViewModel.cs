using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudentDesktop.ViewModels;

// One open tab in the shell's tab strip. Wraps an existing feature view-model (Calendar,
// Assignments, ...) rather than owning it — ShellViewModel still constructs/keeps every
// feature VM alive for the session; a tab just points at one of them.
public partial class AppTabViewModel : ViewModelBase
{
    private readonly Action<AppTabViewModel>? _onClose;

    public string Title { get; }
    public string Icon { get; }
    public ViewModelBase Content { get; }
    public bool IsClosable { get; }

    [ObservableProperty]
    private bool _isSelected;

    public AppTabViewModel(string title, string icon, ViewModelBase content, bool isClosable, Action<AppTabViewModel>? onClose = null)
    {
        Title = title;
        Icon = icon;
        Content = content;
        IsClosable = isClosable;
        _onClose = onClose;
    }

    [RelayCommand]
    private void Close() => _onClose?.Invoke(this);
}
