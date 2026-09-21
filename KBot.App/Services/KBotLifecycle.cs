using KBot.App.BotBrain;
using KBot.App.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace KBot.App.Services;

public sealed class KBotLifecycle : IDisposable
{
    private readonly GameLibrary _library = new();
    private readonly GameLauncher _launcher = new();
    private readonly GameProcessWatcher _watcher = new();
    private readonly NativeService _native = new();
    private readonly PositionOffsetStore _offsetStore = new();
    private readonly ICharacterSessionDetector _characterDetector;
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private bool _disposed;
    private string? _loggedReaderStatus;
    private bool? _launcherWasRunning;

    public KBotLifecycle(ICharacterSessionDetector? characterDetector = null)
    {
        _characterDetector = characterDetector ?? new CharacterSessionDetector();
    }

    public KBotLifecycleState State { get; private set; } = KBotLifecycleState.Booting;
    public string Message { get; private set; } = "Iniciando KBot...";
    public GameInstallation? Installation { get; private set; }
    public LauncherSession? LauncherSession { get; private set; }
    public GameSession? GameSession { get; private set; }
    public CharacterSession? CharacterSession { get; private set; }
    public CharacterDetection? LastDetection { get; private set; }
    public NativeStatus? LastNativeStatus { get; private set; }
    public string HandoffStatus { get; private set; } = "Aguardando launcher/cliente.";
    public bool CanShowMainWindow => State == KBotLifecycleState.Ready && CharacterSession?.IsInGame == true;
    public KBot.App.BotBrain.BotBrain? Bot { get; private set; } = null;
    public string BotLog => Bot?.Log ?? "Brain aguardando personagem.";
    public string BotSignals => Bot?.SignalSummary ?? "sem leitura ainda.";
    public string BotPending => Bot?.PendingCommand ?? "—";
    private BotProfile _profile = new();
    public event Action<KBotLifecycle>? Changed;

    public string StartCore() => _native.StartCore();

    public async Task RestartAsync(GameInstallation? selected = null)
    {
        if (_disposed) return;
        _cancellation?.Cancel();
        if (_runTask is not null) await _runTask;
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _runTask = RunAsync(selected, _cancellation.Token);
    }

    private async Task RunAsync(GameInstallation? selected, CancellationToken cancellationToken)
    {
        GameSession? session = null;
        try
        {
            CharacterSession = null;
            LastDetection = null;
            LastNativeStatus = null;
            _characterDetector.Reset();
            _loggedReaderStatus = null;
            _launcherWasRunning = null;
            HandoffStatus = "Aguardando launcher/cliente.";
            GameSession = null;
            LauncherSession = null;
            SetState(KBotLifecycleState.Booting, "Verificando PokeAlliance...");
            var installation = selected ?? _library.Load().FirstOrDefault(c =>
                c.Name.Equals("PokeAlliance", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(c.LauncherPath).Contains("PokeAlliance", StringComparison.OrdinalIgnoreCase));
            if (installation is null)
            {
                Installation = null;
                SetState(KBotLifecycleState.ClientNotConfigured, "Configure o launcher do PokeAlliance para começar.");
                return;
            }
            Installation = installation;
            Changed?.Invoke(this);
            _native.StartCore();

            var launcher = _launcher.FindRunning(installation);
            var found = _watcher.FindRunning(installation, launcher);
            if (!found.HasValue)
            {
                if (launcher is null)
                {
                    SetState(KBotLifecycleState.LaunchingClient, "Iniciando PokeAlliance...");
                    launcher = _launcher.Launch(installation);
                }
                LauncherSession = launcher;
                HandoffStatus = $"Launcher PID {launcher.LauncherPid}; aguardando cliente real.";
                Trace.WriteLine($"[Handoff] Launcher opened pid={launcher.LauncherPid}; waiting for game process");
                SetState(KBotLifecycleState.WaitingForProcess, "Launcher aberto. Aguardando o jogo...");
                found = await _watcher.WaitForGameAsync(installation, launcher, cancellationToken);
            }
            else LauncherSession = launcher;

            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(found.Value.Path, installation.LauncherPath, StringComparison.OrdinalIgnoreCase))
                LauncherSession = null; // The selected executable is the game itself.
            SetState(KBotLifecycleState.ProcessDetected, "Cliente encontrado. Validando conexão...");
            SetState(KBotLifecycleState.WaitingForWindow, "Validando janela do cliente...");
            session = new GameSession(found.Value.Process, found.Value.Handle, found.Value.Path);
            GameSession = session;
            HandoffStatus = LauncherSession is null
                ? $"Cliente direto: GameSession PID {session.Pid}."
                : $"Launcher PID {LauncherSession.LauncherPid} → Game PID {session.Pid}.";
            Trace.WriteLine($"[Handoff] Game process detected pid={session.Pid} hwnd=0x{session.WindowHandle.ToInt64():X}; launcherPid={LauncherSession?.LauncherPid}; GameSession attached to game PID");
            if (!await _native.AttachGameAsync(session.Pid, Path.GetFileName(session.ExecutablePath), cancellationToken))
                throw new InvalidOperationException("O núcleo nativo não conseguiu conectar ao cliente.");

            var executableName = Path.GetFileName(session.ExecutablePath);
            if (_offsetStore.Get(executableName) is { } savedOffset && savedOffset != 0)
            {
                await _native.SetPositionOffsetAsync(savedOffset, cancellationToken);
                Trace.WriteLine($"[Handoff] Reaplicando offset de posição salvo {savedOffset:X} para {executableName}");
            }

            installation.LastGameExecutable = session.ExecutablePath;
            if (!installation.GameExecutableNames.Contains(executableName, StringComparer.OrdinalIgnoreCase))
                installation.GameExecutableNames.Add(executableName);
            _library.Save(installation);
            SetState(KBotLifecycleState.ClientConnected, "Cliente conectado. Aguardando personagem...");

            while (!cancellationToken.IsCancellationRequested)
            {
                UpdateLauncherHandoff(session);
                if (!session.IsAlive)
                {
                    CharacterSession = new CharacterSession(session.Pid, CharacterPresence.Disconnected, DateTime.Now);
                    LastDetection = null;
                    LastNativeStatus = null;
                    SetState(KBotLifecycleState.Disconnected, "PokeAlliance foi fechado.");
                    return;
                }
                var nativeStatus = await _native.GetStatusAsync(cancellationToken);
                LastNativeStatus = nativeStatus;
                var detection = _characterDetector.Detect(session, nativeStatus);
                var readerLog = $"{detection.ReaderStatus}: {detection.ReaderMessage}";
                if (_loggedReaderStatus != readerLog)
                {
                    _loggedReaderStatus = readerLog;
                    Trace.WriteLine($"[ClientReader] pid={session.Pid}; {readerLog}");
                }
                LastDetection = detection;
                var presence = detection.State;
                var oldPresence = CharacterSession?.State;
                CharacterSession = new CharacterSession(session.Pid, presence, DateTime.Now,
                    detection.Confidence, detection.Source,
                    detection.DetectionStatus, detection.LastConfirmedInGame);
                if (oldPresence != presence)
                    Trace.WriteLine($"[CharacterDetector] {oldPresence} -> {presence}; source={detection.Source}; confidence={detection.Confidence:P0}; reader={detection.ReaderStatus}; vision={detection.VisionSignals}/{detection.VisionSignalTotal}");
                var (state, message) = presence switch
                {
                    CharacterPresence.LoginScreen => (KBotLifecycleState.WaitingForLogin, "Aguardando login no PokeAlliance..."),
                    CharacterPresence.CharacterSelection => (KBotLifecycleState.WaitingForCharacter, "Aguardando seleção de personagem..."),
                    CharacterPresence.Loading => (KBotLifecycleState.WaitingForCharacter, "Carregando personagem..."),
                    CharacterPresence.InGame => (KBotLifecycleState.Ready, "Personagem detectado. Iniciando KBot..."),
                    _ => (KBotLifecycleState.WaitingForCharacter,
                        "Cliente conectado. Aguardando personagem...")
                };
                if (detection.DetectionStatus == CharacterDetectionStatus.CaptureUnavailable &&
                    presence != CharacterPresence.InGame)
                    message = "Cliente conectado. Captura indisponível; aguardando janela...";
            var stateChanged = state != State || message != Message;
            SetState(state, message);
            if (!stateChanged) Changed?.Invoke(this);

            TickBrain(session);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            SetState(KBotLifecycleState.Error, $"Não foi possível iniciar PokeAlliance: {ex.Message}");
        }
        finally
        {
            if (session is not null)
            {
                if (!_disposed) await _native.DetachGameAsync(CancellationToken.None);
                session.Dispose();
                GameSession = null;
            }
        }
    }

    private void TickBrain(GameSession session)
    {
        if (State != KBotLifecycleState.Ready)
        {
            Bot?.Dispose();
            Bot = null;
            return;
        }

        if (Bot is null)
        {
            _profile = BotProfileService.Load();
            Bot = BotBrainFactory.Build(_native, session.WindowHandle, _profile,
                () => GameStateProvider.From(LastNativeStatus, CharacterSession?.State ?? CharacterPresence.Unknown, Environment.TickCount64));
            AutomationEventHub.Shared.Publish(AutomationEventSeverity.Info, "Automation", "brain_started",
                "Motor de automação iniciado para o personagem detectado.", session.Pid.ToString());
        }
        else
        {
            try { Bot.RefreshProfile(BotProfileService.Load()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        Bot.Tick();
    }

    private void UpdateLauncherHandoff(GameSession session)
    {
        if (LauncherSession is not { } launcher) return;
        var running = false;
        try
        {
            using var process = Process.GetProcessById(launcher.LauncherPid);
            running = !process.HasExited && process.ProcessName.Equals(
                Path.GetFileNameWithoutExtension(launcher.LauncherPath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }
        if (_launcherWasRunning == running) return;
        _launcherWasRunning = running;
        HandoffStatus = running
            ? $"Launcher PID {launcher.LauncherPid} aberto; GameSession PID {session.Pid}."
            : $"Launcher PID {launcher.LauncherPid} encerrou; GameSession PID {session.Pid} continua.";
        Trace.WriteLine($"[Handoff] {HandoffStatus}");
    }

    private void SetState(KBotLifecycleState state, string message)
    {
        if (_disposed || (State == state && Message == message)) return;
        State = state;
        Message = message;
        var severity = state switch
        {
            KBotLifecycleState.Error => AutomationEventSeverity.Error,
            KBotLifecycleState.Disconnected or KBotLifecycleState.ClientNotConfigured => AutomationEventSeverity.Warning,
            _ => AutomationEventSeverity.Info
        };
        AutomationEventHub.Shared.Publish(severity, "Session", state.ToString(), message,
            GameSession?.Pid.ToString() ?? string.Empty, TimeSpan.FromSeconds(1));
        Changed?.Invoke(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation?.Cancel();
        _native.Dispose();
    }
}
