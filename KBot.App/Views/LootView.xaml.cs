using System.Windows.Controls;
using KBot.App.ViewModels;

namespace KBot.App.Views;

public partial class LootView : UserControl
{
    public LootView(SettingsViewModel profile)
    {
        InitializeComponent();
        DataContext = profile;
    }
}
