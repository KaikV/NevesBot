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
    private readonly IScreenScanSource _screenScanSource;
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private bool _disposed;
    private string? _loggedReaderStatus;
    private bool? _launcherWasRunning;
    private bool _clientAttached;
    private int _badCoreTicks;
    private DateTime _lastCalibrationAttempt = DateTime.MinValue;
    private readonly OffsetAutoCalibrator _calibrator = new();

    public string CalibrationStatus => _calibrator.Status;

    public KBotLifecycle(ICharacterSessionDetector? characterDetector = null, IScreenScanSource? screenScanSource = null)
    {
        _characterDetector = characterDetector ?? new CharacterSessionDetector();
        _screenScanSource = screenScanSource ?? new NoScreenScanSource();
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
        var firstPass = true;
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
            var installations = _library.Load();
            var installation = selected ??
                installations.FirstOrDefault(c =>
                    c.Name.Equals("PokeAlliance", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(c.LauncherPath).Contains("PokeAlliance", StringComparison.OrdinalIgnoreCase)) ??
                installations.FirstOrDefault();
            if (installation is null)
            {
                Installation = null;
                SetState(KBotLifecycleState.ClientNotConfigured, "Configure o launcher do PokeAlliance para começar.");
                return;
            }
            Installation = installation;
            Changed?.Invoke(this);
            _native.StartCore();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (session is not null)
                {
                    if (!_disposed) await _native.DetachGameAsync(CancellationToken.None);
                    session.Dispose();
                    Bot?.Dispose();
                    Bot = null;
                    _characterDetector.Reset();
                    _loggedReaderStatus = null;
                    _launcherWasRunning = null;
                    CharacterSession = null;
                    LastDetection = null;
                }
                session = null;
                GameSession = null;
                LauncherSession = null;
                _badCoreTicks = 0;
                _clientAttached = false;

                var launcher = _launcher.FindRunning(installation);
                var found = _watcher.FindRunning(installation, launcher);
                if (!found.HasValue)
                {
                    var reconnecting = !firstPass;
                    if (launcher is null && !reconnecting)
                    {
                        SetState(KBotLifecycleState.LaunchingClient, "Iniciando PokeAlliance...");
                        launcher = _launcher.Launch(installation);
                    }
                    LauncherSession = launcher;
                    HandoffStatus = launcher is null
                        ? (reconnecting ? "Cliente fechado. Aguardando você reabrir o PokeAlliance..." : "Cliente não aberto. Aguardando você iniciar o PokeAlliance...")
                        : $"Launcher PID {launcher.LauncherPid}; aguardando cliente real.";
                    Trace.WriteLine($"[Handoff] Waiting for game process (launcherPid={launcher?.LauncherPid ?? 0}); reconnect={reconnecting}");
                    SetState(KBotLifecycleState.WaitingForProcess, launcher is null
                        ? "Aguardando o PokeAlliance abrir..."
                        : "Launcher aberto. Aguardando o jogo...");
                    if (reconnecting)
                    {
                        while (!cancellationToken.IsCancellationRequested && !found.HasValue)
                        {
                            found = _watcher.FindRunning(installation, launcher);
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        }
                    }
                    else
                    {
                        found = await _watcher.WaitForGameAsync(installation, launcher!, cancellationToken);
                    }
                }
                else LauncherSession = launcher;

                cancellationToken.ThrowIfCancellationRequested();
                if (!found.HasValue) continue;
                var resolvedGame = found.Value;
                if (string.Equals(resolvedGame.Path, installation.LauncherPath, StringComparison.OrdinalIgnoreCase))
                    LauncherSession = null; // The selected executable is the game itself.
                firstPass = false;
                SetState(KBotLifecycleState.ProcessDetected, "Cliente encontrado. Validando conexão...");
                SetState(KBotLifecycleState.WaitingForWindow, "Validando janela do cliente...");
                session = new GameSession(resolvedGame.Process, resolvedGame.Handle, resolvedGame.Path);
                GameSession = session;
                HandoffStatus = LauncherSession is null
                    ? $"Cliente direto: GameSession PID {session.Pid}."
                    : $"Launcher PID {LauncherSession.LauncherPid} → Game PID {session.Pid}.";
                Trace.WriteLine($"[Handoff] Game process detected pid={session.Pid} hwnd=0x{session.WindowHandle.ToInt64():X}; launcherPid={LauncherSession?.LauncherPid}; GameSession attached to game PID");
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!_clientAttached &&
                        await _native.AttachGameAsync(session.Pid, Path.GetFileName(session.ExecutablePath), cancellationToken))
                    {
                        _clientAttached = true;
                        Trace.WriteLine($"[ClientReader] ATTACHED ok para pid={session.Pid}");
                    }
                    if (_clientAttached || !session.IsAlive) break;
                    Trace.WriteLine($"[ClientReader] Attach pendente; tentando novamente em 3s (pid={session.Pid})");
                    var coreAlive = await _native.PingAsync();
                    SetState(KBotLifecycleState.WaitingForWindow, coreAlive
                        ? "Conectando ao núcleo do cliente..."
                        : "Núcleo nativo sem resposta (KBot.Native.exe). Verifique se foi compilado...");
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    _native.StartCore();
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!_clientAttached)
                {
                    SetState(KBotLifecycleState.Disconnected, "Cliente aberto, mas o núcleo ainda não conectou. O KBot vai continuar tentando...");
                    Trace.WriteLine($"[ClientReader] Attach ainda não pronto para pid={session.Pid}; mantendo ciclo de reconexão");
                    continue;
                }
                await ReapplySavedOffsetAsync(session, cancellationToken);

                installation.LastGameExecutable = session.ExecutablePath;
                var executableName = Path.GetFileName(session.ExecutablePath);
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
                        SetState(KBotLifecycleState.Disconnected, "PokeAlliance foi fechado. Aguardando reabertura...");
                        Trace.WriteLine($"[Handoff] Game process closed pid={session.Pid}; entering reconnect poll");
                        break;
                    }
                    var nativeStatus = await _native.GetStatusAsync(cancellationToken);
                    LastNativeStatus = nativeStatus;
                    if (nativeStatus is null)
                    {
                        _badCoreTicks++;
                        if (_badCoreTicks >= 3)
                        {
                            Trace.WriteLine("[ClientReader] Núcleo nativo sem resposta há 3 ticks; recriando núcleo");
                            _badCoreTicks = 0;
                            _native.StartCore();
                            if (await _native.PingAsync())
                            {
                                Trace.WriteLine("[ClientReader] Núcleo reviveu; anexando memória de novo");
                                var name = Path.GetFileName(session.ExecutablePath);
                                if (await _native.AttachGameAsync(session.Pid, name, cancellationToken))
                                    await ReapplySavedOffsetAsync(session, cancellationToken);
                                SetState(KBotLifecycleState.ClientConnected, "Núcleo reconectado. Verificando cliente...");
                            }
                        }
                    }
                    else _badCoreTicks = 0;
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
                    MaybeStartAutoCalibration(session, detection, cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
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
                Bot?.Dispose();
                Bot = null;
            }
        }
    }

    private async Task ReapplySavedOffsetAsync(GameSession session, CancellationToken cancellationToken)
    {
        var executableName = Path.GetFileName(session.ExecutablePath);
        if (_offsetStore.Get(executableName) is { } savedOffset && savedOffset != 0)
        {
            await _native.SetPositionOffsetAsync(savedOffset, cancellationToken);
            Trace.WriteLine($"[Handoff] Reaplicando offset de posição salvo {savedOffset:X} para {executableName}");
        }
    }

    // Self-healing: when the character is in-game but the reader is NOT ready
    // (a client update moved the position field), kick the delta-scan calibrator
    // on a background task so the main loop never blocks. A 5-minute cooldown and
    // a running-flag keep it from spamming; the cooldown resets each new session
    // (see _clientAttached block) so a relog gets a fresh attempt.
    private void MaybeStartAutoCalibration(GameSession session, CharacterDetection detection, CancellationToken cancellationToken)
    {
        if (detection.State != CharacterPresence.InGame ||
            detection.ReaderStatus == "READY" ||
            _calibrator.IsRunning ||
            (DateTime.Now - _lastCalibrationAttempt) < TimeSpan.FromMinutes(5))
            return;
        _ = Task.Run(async () =>
        {
            try { await StartCalibrationAsync(session, cancellationToken, force: false); }
            catch (OperationCanceledException) { }
        }, cancellationToken);
    }

    // Manual trigger ("Recalibrar agora" on the dashboard): same pipeline as the
    // automatic one but ignores the cooldown; still refuses while already running
    // or while there is no in-game character attached.
    public async Task<bool> RequestCalibrationAsync(CancellationToken cancellationToken = default)
    {
        var session = GameSession;
        var detection = LastDetection;
        if (session is not { IsAlive: true } ||
            detection is null || detection.State != CharacterPresence.InGame ||
            _calibrator.IsRunning)
            return false;
        try
        {
            return await Task.Run(() => StartCalibrationAsync(session, cancellationToken, force: true), cancellationToken);
        }
        catch (OperationCanceledException) { return false; }
    }

    private async Task<bool> StartCalibrationAsync(GameSession session, CancellationToken cancellationToken, bool force)
    {
        if (!force && (DateTime.Now - _lastCalibrationAttempt) < TimeSpan.FromMinutes(5)) return false;
        _lastCalibrationAttempt = DateTime.Now;
        var executableName = Path.GetFileName(session.ExecutablePath);
        Trace.WriteLine($"[OffsetAutoCalibrator] {(force ? "manual" : "gatilho automático")}: reader={LastDetection?.ReaderStatus}; iniciando recalibração");
        AutomationEventHub.Shared.Publish(AutomationEventSeverity.Info, "Calibration", force ? "manual_started" : "auto_started",
            "Leitura de posição fora do ar; recalibrando offset automaticamente.", session.Pid.ToString());
        var result = false;
        try
        {
            result = await _calibrator.RunAsync(_native, executableName, _offsetStore, cancellationToken);
            AutomationEventHub.Shared.Publish(
                result ? AutomationEventSeverity.Info : AutomationEventSeverity.Warning,
                "Calibration", result ? "auto_ok" : "auto_failed", _calibrator.Status, session.Pid.ToString());
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
        {
            _calibrator.ReportStatus($"Falha na recalibração: {ex.Message}");
        }
        if (!_disposed) Changed?.Invoke(this);
        return result;
    }

    private void TickBrain(GameSession session)
    {
        // While the calibrator owns the character (snap -> 1-tile step -> stable),
        // NO module may walk him or the delta baseline is corrupted. The bot keeps
        // living; only its ticks are held until calibration ends.
        if (_calibrator.IsRunning)
            return;
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
                () =>
                {
                    var presence = CharacterSession?.State ?? CharacterPresence.Unknown;
                    var scan = _screenScanSource.GetScan(LastNativeStatus, presence);
                    return GameStateProvider.From(LastNativeStatus, presence, Environment.TickCount64, scan);
                });
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
