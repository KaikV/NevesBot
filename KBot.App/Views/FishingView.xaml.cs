using System.Windows.Controls;
using KBot.App.ViewModels;

namespace KBot.App.Views;

public partial class FishingView : UserControl
{
    public FishingView(SettingsViewModel profile)
    {
        InitializeComponent();
        DataContext = profile;
    }
}
