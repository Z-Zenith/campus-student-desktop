using CommunityToolkit.Mvvm.ComponentModel;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ApiClient _apiClient = new();

    // SDA-12: exposed so the window's code-behind can fire an exit-ping on focus-loss/close
    // without the view needing its own copy of session/auth state.
    public ApiClient ApiClient => _apiClient;

    // SDA-11: shared across the app's lifetime so it survives sign-out/sign-in and can
    // be attached once to the main window in App.axaml.cs. No assignment-editing view
    // calls BeginSession on it yet — see AssignmentAutoSubmitService for details.
    public AssignmentAutoSubmitService AutoSubmitService { get; }

    // Applied immediately on construction (before any page is shown) and toggled from the
    // shell's side rail once signed in — see ShellViewModel/ShellView.
    public ThemeService ThemeService { get; } = new();

    [ObservableProperty]
    private ViewModelBase _currentPage;

    public MainWindowViewModel()
    {
        AutoSubmitService = new AssignmentAutoSubmitService(_apiClient);
        _currentPage = CreateLoginPage();
    }

    private LoginViewModel CreateLoginPage() => new((identifier, password) =>
    {
        CurrentPage = new TotpViewModel(_apiClient, identifier, password,
            response => CurrentPage = new ShellViewModel(_apiClient, response.UserId, response.FullName, () => CurrentPage = CreateLoginPage(), AutoSubmitService, ThemeService),
            () => CurrentPage = CreateLoginPage());
    });
}
