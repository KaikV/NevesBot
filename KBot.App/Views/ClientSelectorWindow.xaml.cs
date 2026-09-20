using KBot.App.Models;
using KBot.App.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace KBot.App.Views;

public partial class ClientSelectorWindow : Window
{
    private readonly GameLibrary _store = new();
    public GameInstallation? SelectedClient { get; private set; }

    public ClientSelectorWindow(GameInstallation? current, bool firstRun = false)
    {
        InitializeComponent();
        RefreshClients(current);
        if (firstRun)
        {
            Title = "Configurar PokeAlliance";
            Width = 540;
            Height = 300;
            HeaderText.Text = "CONFIGURAR POKEALLIANCE";
            HeadingText.Text = "Selecione o launcher oficial";
            AddClientButton.Visibility = Visibility.Collapsed;
            AddPanel.Visibility = Visibility.Visible;
            FolderButton.Visibility = Visibility.Collapsed;
            ClientsList.Visibility = Visibility.Collapsed;
            ClientsList.SelectedItem = null;
            SelectedClient = null;
            ClientPathText.Text = "Selecione o launcher oficial do PokeAlliance.";
            StatusText.Text = string.Empty;
            OpenButton.IsEnabled = false;
            OpenButton.Content = "SALVAR E INICIAR";
        }
    }

    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Executáveis (*.exe)|*.exe", Title = "Selecionar launcher" };
        if (dialog.ShowDialog(this) == true) AddLauncher(Path.GetFullPath(dialog.FileName), Path.GetDirectoryName(dialog.FileName)!);
    }

    private void AddClick(object sender, RoutedEventArgs e) => AddPanel.Visibility = Visibility.Visible;

    private void FolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Selecionar pasta do OTServer" };
        if (dialog.ShowDialog(this) != true) return;
        var root = dialog.FolderName;
        var directories = new[] { root }.Concat(new[] { "bin", "launcher", "client" }
            .Select(name => Path.Combine(root, name)).Where(Directory.Exists));
        var candidates = directories.SelectMany(directory => Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
            .Where(path => { var name = Path.GetFileNameWithoutExtension(path);
                return name.Contains("launcher", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("client", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("ot", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(Path.GetFileName(root), StringComparison.OrdinalIgnoreCase); })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => Path.GetFileName(path).Contains("launcher", StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path).Take(30).ToList();
        if (candidates.Count == 0) { StatusText.Text = "Nenhum launcher encontrado nessa pasta."; return; }
        var choice = new Window { Title = "Escolha o launcher", Width = 480, Height = 280,
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = FindResource("WindowBackground") as System.Windows.Media.Brush };
        var panel = new DockPanel { Margin = new Thickness(18) };
        var list = new ListBox { ItemsSource = candidates, SelectedIndex = 0 };
        var confirm = new Button { Content = "USAR ESTE LAUNCHER", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        confirm.Click += (_, _) => { choice.DialogResult = true; choice.Close(); };
        DockPanel.SetDock(confirm, Dock.Bottom);
        panel.Children.Add(confirm);
        panel.Children.Add(list);
        choice.Content = panel;
        if (choice.ShowDialog() == true && list.SelectedItem is string path) AddLauncher(path, root);
    }

    private void AddLauncher(string path, string directory)
    {
        var client = new GameInstallation { Name = GameInstallation.FriendlyName(path, directory), LauncherPath = path, InstallDirectory = directory };
        _store.Save(client);
        RefreshClients(client);
        AddPanel.Visibility = Visibility.Collapsed;
    }

    private void RefreshClients(GameInstallation? selected)
    {
        var clients = _store.Load();
        ClientsList.ItemsSource = clients;
        ClientsList.SelectedItem = clients.FirstOrDefault(c => c.Id == selected?.Id ||
            string.Equals(c.LauncherPath, selected?.LauncherPath, StringComparison.OrdinalIgnoreCase));
        if (ClientsList.SelectedItem is null && clients.Count > 0) ClientsList.SelectedIndex = 0;
    }

    private void ClientSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedClient = ClientsList.SelectedItem as GameInstallation;
        ClientPathText.Text = SelectedClient?.LauncherPath ?? "Selecione o launcher ou a pasta do seu OTServer.";
        StatusText.Text = SelectedClient is null ? "" : "Pronto para iniciar";
        OpenButton.IsEnabled = SelectedClient is not null && File.Exists(SelectedClient.LauncherPath);
    }

    private void OpenClick(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void CancelClick(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
