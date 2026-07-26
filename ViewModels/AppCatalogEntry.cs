namespace StudentDesktop.ViewModels;

// One entry in the app launcher — drives both the left rail's icon buttons and the Home
// tab's tile grid, so there's exactly one place that lists "the apps in this shell".
public sealed record AppCatalogEntry(string Title, string Icon, ViewModelBase ViewModel);
