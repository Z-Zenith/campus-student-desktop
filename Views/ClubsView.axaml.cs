using System.ComponentModel;
using Avalonia.Controls;
using StudentDesktop.ViewModels;

namespace StudentDesktop.Views;

// New "browse and join clubs" feature. ClubHomeSiteWebView renders a club's HomeSiteHtml —
// arbitrary, untrusted HTML/CSS/JS authored by that club's own student/faculty leadership —
// so this is a brand-new NativeWebView instance, entirely separate from BrowserView's: no
// CodeBridge/DmsBridge/SekBridge is ever wired to it, and (unlike BrowserView's whitelist
// check) every single navigation after the one .NET itself initiates via .Html is cancelled,
// since untrusted club content has no legitimate reason to navigate anywhere.
public partial class ClubsView : UserControl
{
    private ClubsViewModel? _viewModel;
    private bool _allowNextNavigation;

    public ClubsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = DataContext as ClubsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        LoadSelectedClubHomeSite();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClubsViewModel.SelectedClub))
        {
            LoadSelectedClubHomeSite();
        }
    }

    private void LoadSelectedClubHomeSite()
    {
        // Exactly one navigation is ever permitted per NavigateToString call: the one this
        // line triggers. Set the flag immediately before calling so there is no window for
        // OnNavigationStarted to see it set for any navigation but this one.
        // (NativeWebView 12.0.1 has no settable "Html" property — NavigateToString(text,
        // baseUri) is the actual API for loading raw HTML content, confirmed via reflection
        // on the installed package.)
        _allowNextNavigation = true;
        ClubHomeSiteWebView.NavigateToString(_viewModel?.SelectedClub?.HomeSiteHtml ?? "");
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (_allowNextNavigation)
        {
            _allowNextNavigation = false;
            return;
        }
        e.Cancel = true;
    }
}
