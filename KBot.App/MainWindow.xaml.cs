using System.Windows;
using System.Threading.Tasks;
using KBot.App.ViewModels;
using KBot.App.Services;

namespace KBot.App
{
    public partial class MainWindow : Window
    {
        public MainWindow(KBotLifecycle lifecycle)
        {
            InitializeComponent();
            DataContext = new MainViewModel(lifecycle);
        }

        private async void OnClosed(object? sender, System.EventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.DisposeAsync();
            }
        }
    }
}
