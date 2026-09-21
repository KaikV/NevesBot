using System.Windows.Controls;
using KBot.App.ViewModels;

namespace KBot.App.Views;

public partial class TargetView : UserControl
{
    public TargetView(SettingsViewModel profile)
    {
        InitializeComponent();
        DataContext = profile;
    }
}
