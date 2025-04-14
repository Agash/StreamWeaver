using Microsoft.UI.Xaml.Controls;
using StreamWeaver.UI.ViewModels;

namespace StreamWeaver.UI.Views.SettingsPages;

/// <summary>
/// Page to display loaded plugins.
/// </summary>
public sealed partial class PluginsSettingsPage : Page
{
    public SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    public PluginsSettingsPage() => this.InitializeComponent();
}
