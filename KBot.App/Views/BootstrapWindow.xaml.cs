using KBot.App.Models;
using KBot.App.Services;
using KBot.App.ViewModels;
using System.Windows;

namespace KBot.App.Views;

public partial class BootstrapWindow : Window
{
    private readonly KBotLifecycle _lifecycle;
    private bool _firstRunDialogShown;
    private DetectionDebugWindow? _detectionWindow;

    public BootstrapWindow(KBotLifecycle lifecycle)
    {
        InitializeComponent();
        _lifecycle = lifecycle;
        _lifecycle.Changed += OnLifecycleChanged;
        Closed += (_, _) => _lifecycle.Changed -= OnLifecycleChanged;
        UpdateView();
    }

    private void OnLifecycleChanged(KBotLifecycle lifecycle)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdateView();
            if (lifecycle.State == KBotLifecycleState.ClientNotConfigured && !_firstRunDialogShown)
            {
                _firstRunDialogShown = true;
                ConfigureClick(this, new RoutedEventArgs());
            }
        });
    }

    private void UpdateView()
    {
        ClientNameText.Text = _lifecycle.Installation?.Name ?? "PokeAlliance";
        StatusText.Text = _lifecycle.Message;
        SelectedExecutableText.Text = _lifecycle.Installation is { } installation
            ? $"Cliente selecionado: {installation.LauncherPath}" : "Nenhum executável selecionado.";
        var gameSession = _lifecycle.GameSession;
        var nativeStatus = _lifecycle.LastNativeStatus;
        ClientStateText.Text = gameSession?.IsAlive == true
            ? nativeStatus is { NativeOnline: true, ClientFound: true } && nativeStatus.Pid == gameSession.Pid
                ? "Cliente: Conectado" : "Cliente: Aberto"
            : _lifecycle.State == KBotLifecycleState.LaunchingClient ? "Cliente: Iniciando"
            : _lifecycle.State == KBotLifecycleState.WaitingForProcess ? "Cliente: Aberto (launcher)"
            : "Cliente: Fechado";
        ConfigureButton.Visibility = _lifecycle.State == KBotLifecycleState.ClientNotConfigured ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = _lifecycle.State is KBotLifecycleState.Disconnected or KBotLifecycleState.Error
            ? Visibility.Visible : Visibility.Collapsed;
        ChangeButton.Visibility = _lifecycle.Installation is null ? Visibility.Collapsed : Visibility.Visible;
        WaitingProgress.Visibility = _lifecycle.State is KBotLifecycleState.ClientNotConfigured or
            KBotLifecycleState.Disconnected or KBotLifecycleState.Error ? Visibility.Collapsed : Visibility.Visible;
        var game = _lifecycle.GameSession;
        var character = _lifecycle.CharacterSession;
        var detection = _lifecycle.LastDetection;
        var hwnd = game?.WindowHandle ?? 0;
        DetectionDiagnosticText.Text =
            $"GameSession  {(game?.IsAlive == true ? "Connected" : "Disconnected")}\n" +
            $"Launcher PID {_lifecycle.LauncherSession?.LauncherPid.ToString() ?? "—"}\n" +
            $"PID          {(game?.Pid.ToString() ?? "—")}\n" +
            $"HWND         {(hwnd != 0 ? $"0x{hwnd.ToInt64():X}" : "—")}\n" +
            $"Capture      {(detection?.Frame is not null ? "OK" : detection?.VisionStatus ?? "Waiting")}\n" +
            $"ClientReader {detection?.ReaderStatus ?? "Waiting"}\n" +
            $"Vision       {(detection is null ? "Waiting" : $"{detection.VisionSignals}/{detection.VisionSignalTotal} signals")}\n" +
            $"Character    {character?.State.ToString() ?? "Unknown"}\n" +
            $"Detection    {character?.DetectionStatus.ToString() ?? "Evaluating"}\n" +
            $"Last InGame  {character?.LastConfirmedInGame?.ToString("HH:mm:ss") ?? "—"}\n" +
            $"Confidence   {character?.Confidence ?? 0:P0}\n" +
            $"Source       {character?.DetectionSource.ToString() ?? "None"}\n" +
            $"Handoff      {_lifecycle.HandoffStatus}";
        DetectionDiagnosticText.ToolTip = detection?.ReaderMessage;
    }

    private async void ConfigureClick(object sender, RoutedEventArgs e)
    {
        var selector = new ClientSelectorWindow(null, firstRun: true) { Owner = this };
        if (selector.ShowDialog() == true && selector.SelectedClient is { } client)
        {
            client.Name = "PokeAlliance";
            new GameLibrary().Save(client);
            await _lifecycle.RestartAsync(client);
        }
    }

    private async void ChangeClick(object sender, RoutedEventArgs e)
    {
        var selector = new ClientSelectorWindow(_lifecycle.Installation) { Owner = this };
        if (selector.ShowDialog() == true && selector.SelectedClient is { } client)
            await _lifecycle.RestartAsync(client);
    }

    private async void RetryClick(object sender, RoutedEventArgs e) => await _lifecycle.RestartAsync();

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var settings = new Window
        {
            Title = "Configurações do KBot",
            Width = 820,
            Height = 620,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = FindResource("WindowBackground") as System.Windows.Media.Brush,
            Content = new SettingsView(new SettingsViewModel(), "Settings")
        };
        settings.ShowDialog();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void DetectionClick(object sender, RoutedEventArgs e)
    {
        if (_detectionWindow is { IsVisible: true }) { _detectionWindow.Activate(); return; }
        _detectionWindow = new DetectionDebugWindow(_lifecycle) { Owner = this };
        _detectionWindow.Closed += (_, _) => _detectionWindow = null;
        _detectionWindow.Show();
    }
}
