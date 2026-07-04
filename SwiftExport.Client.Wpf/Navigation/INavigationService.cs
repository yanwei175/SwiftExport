using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SwiftExportClient.Navigation;

public interface INavigationService : INotifyPropertyChanged
{
    ObservableCollection<NavigationTabItem> OpenTabs { get; }

    NavigationTabItem? SelectedTab { get; set; }

    Task OpenAsync(string routeKey);

    void Activate(NavigationTabItem? tab);

    Task CloseAsync(NavigationTabItem? tab);

    void Reset();
}
