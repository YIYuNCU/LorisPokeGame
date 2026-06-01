using System.Windows.Controls;
using System.Windows.Input;
using LolitaPoker.Core.ViewModels;

namespace LolitaPoker.Core.Views;

public partial class NetworkSettingsView : UserControl
{
    public NetworkSettingsView()
    {
        InitializeComponent();
    }

    private void OnLobbyCardClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is NetworkSettingsViewModel vm)
            vm.ShowLobbyCommand.Execute(null);
    }

    private void OnServerBrowserClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is NetworkSettingsViewModel vm)
            vm.BrowseServersCommand.Execute(null);
    }
}
