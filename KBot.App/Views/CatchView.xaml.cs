using System.Windows.Controls;
using KBot.App.ViewModels;

namespace KBot.App.Views;

public partial class CatchView : UserControl
{
    public CatchView(SettingsViewModel profile)
    {
        InitializeComponent();
        DataContext = profile;
    }
}
