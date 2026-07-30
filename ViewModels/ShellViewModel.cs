using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudentDesktop.Services;

namespace StudentDesktop.ViewModels;

// Office-365-style shell: a left icon rail + a tab strip. OpenApp is the one place a tile
// or rail button routes through — if the app's view-model instance already has an open
// tab, that tab is just selected instead of duplicating it. This is also what keeps Notes
// (SekBridge) and Messages (DmsBridge) to a single tab each for free: their WebView
// bridges are constructed once, per-session singletons (see NotesViewModel/
// MessagesViewModel), so a second concurrent mount was never supported — routing every
// open request through "find existing tab for this instance, else create one" means
// there's structurally no way to open a second one.
public partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly Action _onSignOut;
    private bool _disposed;

    public string FullName { get; }
    public ThemeService ThemeService { get; }

    public CalendarViewModel CalendarViewModel { get; }
    public EventsViewModel EventsViewModel { get; }
    public ChangePasswordViewModel ChangePasswordViewModel { get; }
    public MarksViewModel MarksViewModel { get; }
    public AssignmentsViewModel AssignmentsViewModel { get; }
    public CommunityViewModel CommunityViewModel { get; }
    public BrowserViewModel BrowserViewModel { get; }
    public NotesViewModel NotesViewModel { get; }
    public CodeEditorViewModel CodeEditorViewModel { get; }
    public MessagesViewModel MessagesViewModel { get; }
    public TeacherFeedbackViewModel TeacherFeedbackViewModel { get; }
    public CourseInfoViewModel CourseInfoViewModel { get; }

    public ObservableCollection<AppCatalogEntry> AppCatalog { get; } = [];
    public ObservableCollection<AppTabViewModel> OpenTabs { get; } = [];

    [ObservableProperty]
    private AppTabViewModel _selectedTab = null!;

    [ObservableProperty]
    private bool _isRailExpanded = true;

    // SDA-01: owned for the lifetime of the signed-in session. Started here so the
    // class-time lock is active as soon as the student is logged in, and stopped/
    // disposed on sign-out so no restriction (and no timer) lingers past the session.
    public ClassLockService ClassLockService { get; }

    // SDA-25: owned for the lifetime of the signed-in session, same as ClassLockService —
    // it reads ClassLockService.IsLocked and the app-lifetime AssignmentAutoSubmitService
    // to decide whether a class/assignment window is currently active.
    public UsageTelemetryService UsageTelemetryService { get; }

    public ShellViewModel(ApiClient apiClient, Guid userId, string fullName, Action onSignOut,
        AssignmentAutoSubmitService autoSubmitService, ThemeService themeService)
    {
        _apiClient = apiClient;
        _onSignOut = onSignOut;
        FullName = fullName;
        ThemeService = themeService;

        CalendarViewModel = new CalendarViewModel(apiClient);
        EventsViewModel = new EventsViewModel(apiClient);
        ChangePasswordViewModel = new ChangePasswordViewModel(apiClient);
        MarksViewModel = new MarksViewModel(apiClient);
        AssignmentsViewModel = new AssignmentsViewModel(apiClient);
        CommunityViewModel = new CommunityViewModel(apiClient);
        BrowserViewModel = new BrowserViewModel(apiClient);
        NotesViewModel = new NotesViewModel(apiClient, userId);
        CodeEditorViewModel = new CodeEditorViewModel(apiClient, userId);
        // B2 live preview (SDA/SEK plan): CodeBridge stays UI-agnostic (no Browser/Shell
        // reference of its own) — this is the one place that actually knows about both
        // the Coding and Browser apps, so it's where the cross-app "open this preview URL
        // as a browser tab" coordination belongs, not inside either ViewModel directly.
        CodeEditorViewModel.Bridge.PreviewReady += url =>
        {
            OpenApp(new AppCatalogEntry("Browser", "🌐", BrowserViewModel));
            BrowserViewModel.OpenPreviewTab(url);
        };
        MessagesViewModel = new MessagesViewModel(apiClient, userId);
        TeacherFeedbackViewModel = new TeacherFeedbackViewModel(apiClient);
        CourseInfoViewModel = new CourseInfoViewModel(apiClient);

        AppCatalog.Add(new AppCatalogEntry("Calendar", "📅", CalendarViewModel));
        AppCatalog.Add(new AppCatalogEntry("Events", "🎉", EventsViewModel));
        AppCatalog.Add(new AppCatalogEntry("Marks", "📊", MarksViewModel));
        AppCatalog.Add(new AppCatalogEntry("Assignments", "📝", AssignmentsViewModel));
        AppCatalog.Add(new AppCatalogEntry("Community", "👥", CommunityViewModel));
        AppCatalog.Add(new AppCatalogEntry("Browser", "🌐", BrowserViewModel));
        AppCatalog.Add(new AppCatalogEntry("Notes", "🗒️", NotesViewModel));
        AppCatalog.Add(new AppCatalogEntry("Coding", "💻", CodeEditorViewModel));
        AppCatalog.Add(new AppCatalogEntry("Messages", "💬", MessagesViewModel));
        AppCatalog.Add(new AppCatalogEntry("Teacher Feedback", "🗣️", TeacherFeedbackViewModel));
        AppCatalog.Add(new AppCatalogEntry("Course Info", "📚", CourseInfoViewModel));
        AppCatalog.Add(new AppCatalogEntry("Change Password", "🔒", ChangePasswordViewModel));

        var homeTab = new AppTabViewModel("Home", "🏠", new HomeViewModel(AppCatalog, OpenAppCommand), isClosable: false);
        OpenTabs.Add(homeTab);
        _selectedTab = homeTab;
        homeTab.IsSelected = true;

        ClassLockService = new ClassLockService(apiClient);
        ClassLockService.Start();

        UsageTelemetryService = new UsageTelemetryService(apiClient, ClassLockService, autoSubmitService);
        UsageTelemetryService.Start();
    }

    partial void OnSelectedTabChanged(AppTabViewModel? oldValue, AppTabViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }
        newValue.IsSelected = true;
    }

    [RelayCommand]
    private void SelectTab(AppTabViewModel tab) => SelectedTab = tab;

    [RelayCommand]
    private void OpenApp(AppCatalogEntry entry)
    {
        var existing = OpenTabs.FirstOrDefault(t => ReferenceEquals(t.Content, entry.ViewModel));
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var tab = new AppTabViewModel(entry.Title, entry.Icon, entry.ViewModel, isClosable: true, CloseTab);
        OpenTabs.Add(tab);
        SelectedTab = tab;
    }

    private void CloseTab(AppTabViewModel tab)
    {
        if (!tab.IsClosable)
        {
            return;
        }

        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);

        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = OpenTabs.ElementAtOrDefault(index) ?? OpenTabs[^1];
        }
    }

    [RelayCommand]
    private void ToggleRail() => IsRailExpanded = !IsRailExpanded;

    [RelayCommand]
    private async Task SignOutAsync()
    {
        try
        {
            await _apiClient.LogoutAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            // Best-effort — the local session is cleared regardless of server-side outcome.
        }
        Dispose();
        _onSignOut();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        ClassLockService.Dispose();
        UsageTelemetryService.Dispose();
    }
}
