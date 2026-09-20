using KBot.App.Models;
using System.Diagnostics;
using System.Windows;

namespace KBot.App.Views;

public partial class ClientFoundWindow : Window
{
    public bool ChooseOther { get; private set; }

    public ClientFoundWindow(GameInstallation client, Process process)
    {
        InitializeComponent();
        ClientText.Text = client.Name;
        PidText.Text = $"PID {process.Id}";
    }

    private void ConnectClick(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void OtherClick(object sender, RoutedEventArgs e) { ChooseOther = true; DialogResult = false; Close(); }
}
