using System.Windows.Controls;
using KBot.App.ViewModels;

namespace KBot.App.Views;

public partial class AlertsView : UserControl
{
    public AlertsView(SettingsViewModel profile)
    {
        InitializeComponent();
        DataContext = profile;
    }
}
