using KBot.App.Models;
using KBot.App.Services;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace KBot.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly KBotLifecycle _lifecycle;
    private BitmapSource? _sessionCapture;
    private string? _captureError;
    private bool _disposed;

    public DashboardViewModel(KBotLifecycle lifecycle)
    {
        _lifecycle = lifecycle;
        StartCoreCommand = new RelayCommand(_ => { _lifecycle.StartCore(); Refresh(); }, _ => CanStartCore);
        OpenSessionCommand = new RelayCommand(_ => _ = _lifecycle.RestartAsync(), _ => !HasSession);
        CaptureSessionCommand = new RelayCommand(_ => CaptureSession(), _ => HasSession);
        _lifecycle.Changed += OnLifecycleChanged;
        Refresh();
    }

    private GameSession? Session => _lifecycle.GameSession;
    private NativeStatus? Status => _lifecycle.LastNativeStatus;
    private bool NativeMatchesSession => Session is { IsAlive: true } session &&
        Status is { NativeOnline: true, ClientFound: true } status && status.Pid == session.Pid;

    public ICommand StartCoreCommand { get; }
    public ICommand OpenSessionCommand { get; }
    public ICommand CaptureSessionCommand { get; }
    public string ClientName => _lifecycle.Installation?.Name ?? "Nenhum cliente configurado";
    public string ConfiguredClientPath => _lifecycle.Installation?.LauncherPath ?? string.Empty;
    public string SessionExecutable => Session is null ? "—" : Path.GetFileName(Session.ExecutablePath);
    public string SessionPidText => Session?.Pid.ToString() ?? "—";
    public string SessionHwndText => Session is null ? "—" : $"0x{Session.WindowHandle.ToInt64():X}";
    public bool HasSession => Session?.IsAlive == true;
    public string OpenClientLabel => HasSession ? "CLIENTE CONECTADO" : "INICIAR JOGO";
    public string SessionMessage => _captureError ?? _lifecycle.Message;
    public BitmapSource? SessionCapture => _sessionCapture;
    public bool HasSessionCapture => _sessionCapture is not null;
    public bool NativeOnline => Status?.NativeOnline == true;
    public bool ClientConnected => NativeMatchesSession;
    public string ProcessName => NativeMatchesSession && !string.IsNullOrWhiteSpace(Status?.ProcessName)
        ? Status.ProcessName! : Session is null ? "—" : Path.GetFileName(Session.ExecutablePath);
    public int? ProcessId => NativeMatchesSession ? Status!.Pid : Session?.Pid;
    public DateTime? LastUpdate => _lifecycle.CharacterSession?.LastUpdated;
    public bool IsMonitoring => _lifecycle.State is KBotLifecycleState.ClientConnected or
        KBotLifecycleState.WaitingForLogin or KBotLifecycleState.WaitingForCharacter or KBotLifecycleState.Ready;
    public string CoreStatusText => NativeOnline ? "Online" : "Offline";
    public string ClientStatusText => ClientConnected ? "Conectado" : HasSession ? "Aberto" : "Fechado";
    public string MonitorStatusText => IsMonitoring ? "Ativo" : "Pausado";
    public bool CanStartCore => !NativeOnline;
    public string ReaderStatus => Status?.ReaderStatus switch
    {
        "READY" => "Pronto",
        "NOT_CONFIGURED" => "Não configurado",
        _ => Status?.ReaderStatus ?? "Desconhecido"
    };
    public string ReaderDetail => ClientConnected
        ? ReaderStatus == "Pronto" && Status is { HasPosition: true } s
            ? $"Reader: Pronto (posição {s.PosX}, {s.PosY}, {s.PosZ})"
            : $"Reader: {ReaderStatus} — {Status?.ReaderMessage ?? "desconhecido"}"
        : "Reader: aguardando conexão.";
    public string StatusMessage => ClientConnected
        ? ReaderStatus == "Pronto"
            ? $"Cliente {ProcessName} conectado e pronto para leitura."
            : $"Cliente {ProcessName} conectado. Leitura interna ainda não configurada.\nMotivo do core: {Status?.ReaderMessage ?? "desconhecido"}"
        : NativeOnline ? "Núcleo online. Aguardando conexão com o cliente."
        : "Núcleo offline. Use “Iniciar núcleo” para tentar conectar.";

    public bool HasPosition => ClientConnected && Status?.HasPosition == true;
    public string PositionText => HasPosition && Status is { } status
        ? $"{status.PosX}, {status.PosY}, {status.PosZ}" : "—";

    // Live bot brain feed (drives the "AUTOMAÇÃO ATIVA" card).
    public string BotActionLog => _lifecycle.BotLog;
    public string BotSignalSummary => _lifecycle.BotSignals;
    public string BotPendingCommand => _lifecycle.BotPending;
    public bool BrainActive => _lifecycle.Bot is not null;

    private void OnLifecycleChanged(KBotLifecycle _)
    {
        if (_disposed) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.InvokeAsync(Refresh);
        else Refresh();
    }

    public void Refresh()
    {
        if (_disposed) return;
        foreach (var property in new[]
        {
            nameof(ClientName), nameof(ConfiguredClientPath), nameof(SessionExecutable),
            nameof(SessionPidText), nameof(SessionHwndText), nameof(HasSession),
            nameof(OpenClientLabel), nameof(SessionMessage), nameof(NativeOnline),
            nameof(ClientConnected), nameof(ProcessName), nameof(ProcessId),
            nameof(LastUpdate), nameof(IsMonitoring), nameof(CoreStatusText),
            nameof(ClientStatusText), nameof(MonitorStatusText),             nameof(CanStartCore),
            nameof(ReaderStatus), nameof(ReaderDetail), nameof(StatusMessage), nameof(HasPosition), nameof(PositionText),
            nameof(BotActionLog), nameof(BotSignalSummary), nameof(BotPendingCommand), nameof(BrainActive)
        }) OnPropertyChanged(property);
        ((RelayCommand)OpenSessionCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CaptureSessionCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StartCoreCommand).RaiseCanExecuteChanged();
    }

    private void CaptureSession()
    {
        if (Session is not { IsAlive: true } session) return;
        try
        {
            _sessionCapture = session.Capture();
            _captureError = null;
            OnPropertyChanged(nameof(SessionCapture));
            OnPropertyChanged(nameof(HasSessionCapture));
            OnPropertyChanged(nameof(SessionMessage));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            _captureError = $"Captura indisponível: {ex.Message}";
            OnPropertyChanged(nameof(SessionMessage));
        }
    }

    public Task DisposeAsync()
    {
        _disposed = true;
        _lifecycle.Changed -= OnLifecycleChanged;
        _sessionCapture = null;
        return Task.CompletedTask;
    }
}
