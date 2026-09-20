using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using KBot.App.ViewModels;
using KBot.App.Services;

namespace KBot.App.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView(KBotLifecycle lifecycle)
        {
            InitializeComponent();
            DataContext = new DashboardViewModel(lifecycle);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is DashboardViewModel viewModel) viewModel.Refresh();
        }

        public async Task DisposeAsync()
        {
            if (DataContext is DashboardViewModel viewModel) await viewModel.DisposeAsync();
        }
    }
}
