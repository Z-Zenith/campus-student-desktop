using System.Collections.ObjectModel;
using System.Windows.Input;

namespace StudentDesktop.ViewModels;

// The pinned "Home" tab: a tile grid of every app in the catalog. Tile click reuses the
// same OpenAppCommand the left rail's buttons call, so there's one behavior for "open an
// app" regardless of which affordance the student used.
public partial class HomeViewModel(ObservableCollection<AppCatalogEntry> catalog, ICommand openAppCommand) : ViewModelBase
{
    public ObservableCollection<AppCatalogEntry> Catalog { get; } = catalog;
    public ICommand OpenAppCommand { get; } = openAppCommand;
}
