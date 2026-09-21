using System.Windows.Controls;
using KBot.App.ViewModels;

namespace KBot.App.Views;

public partial class HealingView : UserControl
{
    public HealingView(SettingsViewModel profile)
    {
        InitializeComponent();
        DataContext = profile;
    }
}
