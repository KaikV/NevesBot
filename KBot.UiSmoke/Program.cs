using KBot.App;
using KBot.App.ViewModels;
using KBot.App.Services;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KBot.UiSmoke;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2) throw new ArgumentException("Informe a pasta de saída dos previews.");
        Directory.CreateDirectory(args[0]);

        if (args.Length == 2 && args[1] == "--handoff-check")
        {
            var installation = new GameLibrary().Load().First(c => c.Name == "PokeAlliance");
            var launcher = new GameLauncher().FindRunning(installation)
                ?? throw new Exception("Launcher não está aberto para o teste de fechamento.");
            var watcher = new GameProcessWatcher();
            var found = watcher.FindRunning(installation, launcher)
                ?? throw new Exception("Cliente real ainda não foi detectado após JOGAR.");
            var gamePid = found.Process.Id;
            var gameHwnd = found.Handle;
            found.Process.Dispose();
            using var native = new NativeService();
            var status = native.GetStatusAsync(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"Launcher PID: {launcher.LauncherPid}; Game PID: {gamePid}; GameSession PID final: {status?.Pid}; HWND: 0x{gameHwnd.ToInt64():X}");
            if (launcher.LauncherPid == gamePid || status?.Pid != gamePid || status.Hwnd == 0)
                throw new Exception("O handoff inicial não anexou o PID/HWND do cliente real.");
            Console.WriteLine("Handoff inicial confirmado. Aguardando o fechamento do launcher...");
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                var launcherStillRunning = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(launcher.LauncherPath))
                    .Any(process => { using (process) return process.Id == launcher.LauncherPid && !process.HasExited; });
                if (!launcherStillRunning)
                {
                    for (var i = 0; i < 5; i++)
                    {
                        Thread.Sleep(1000);
                        var current = watcher.FindRunning(installation, launcher);
                        status = native.GetStatusAsync(CancellationToken.None).GetAwaiter().GetResult();
                        var botAlive = Process.GetProcessesByName("NevesBot").Any(process =>
                        {
                            using (process) return !process.HasExited;
                        });
                        if (current is null || current.Value.Process.Id != gamePid ||
                            status?.Pid != gamePid || !status.ClientFound || !botAlive)
                        {
                            current?.Process.Dispose();
                            throw new Exception($"Desconexão falsa após fechar launcher: game={current?.Process.Id}, native={status?.Pid}, bot={botAlive}.");
                        }
                        current.Value.Process.Dispose();
                    }
                    Console.WriteLine($"Launcher closed; game PID={gamePid} alive; native PID={status?.Pid}; NevesBot alive. False disconnect: NO.");
                    return;
                }
                Thread.Sleep(1000);
            }
            throw new Exception("Launcher permaneceu aberto; fechamento não observado no prazo do teste.");
        }

        if (args.Length == 2 && args[1] == "--handoff-expired-launcher")
        {
            var installation = new GameLibrary().Load().First(c => c.Name == "PokeAlliance");
            var closedLauncher = new KBot.App.Models.LauncherSession(int.MaxValue,
                installation.LauncherPath, DateTime.Now.AddMinutes(-5));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var found = new GameProcessWatcher().WaitForGameAsync(installation, closedLauncher, timeout.Token)
                .GetAwaiter().GetResult();
            using (found.Process)
            {
                if (found.Process.Id == closedLauncher.LauncherPid || found.Handle == 0 ||
                    !Path.GetFileName(found.Path).Equals("PokeAlliance_dx.exe", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Watcher dependeu do launcher já encerrado ou encontrou o processo errado.");
                var reusedPid = new KBot.App.Models.LauncherSession(found.Process.Id,
                    installation.LauncherPath, closedLauncher.StartedAt);
                var afterReuse = new GameProcessWatcher().FindRunning(installation, reusedPid);
                if (afterReuse is null || afterReuse.Value.Process.Id != found.Process.Id)
                    throw new Exception("O watcher descartou o cliente real porque seu PID coincidiu com o PID antigo do launcher.");
                afterReuse.Value.Process.Dispose();
                Console.WriteLine($"Launcher ausente PID={closedLauncher.LauncherPid}; cliente real PID={found.Process.Id}, HWND=0x{found.Handle.ToInt64():X}; handoff sem launcher ativo: PASS.");
            }
            return;
        }

        if (args.Length == 2 && args[1] == "--capture-installed")
        {
            var installation = new GameLibrary().Load().First(c => c.Name == "PokeAlliance");
            var launcher = new GameLauncher().FindRunning(installation);
            var found = new GameProcessWatcher().FindRunning(installation, launcher)
                ?? throw new Exception("Cliente PokeAlliance ainda não encontrado.");
            using (found.Process)
            {
                var frame = WindowCaptureService.Capture(found.Handle);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));
                var path = Path.Combine(args[0], $"pokealliance-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                using var stream = File.Create(path);
                encoder.Save(stream);
                Console.WriteLine($"Captured PID={found.Process.Id}, HWND=0x{found.Handle.ToInt64():X}, {frame.PixelWidth}x{frame.PixelHeight}: {path}");
            }
            return;
        }

        if (args.Length == 2 && args[1] == "--vision-installed")
        {
            using var lifecycle = new KBotLifecycle();
            lifecycle.RestartAsync().GetAwaiter().GetResult();
            Thread.Sleep(6000);
            var detection = lifecycle.LastDetection;
            Console.WriteLine($"Lifecycle={lifecycle.State}; Character={lifecycle.CharacterSession?.State}; Confidence={lifecycle.CharacterSession?.Confidence:P0}; Source={lifecycle.CharacterSession?.DetectionSource}");
            Console.WriteLine($"Reader={detection?.ReaderStatus}; Reason={detection?.ReaderMessage}; Vision={detection?.VisionStatus}");
            if (lifecycle.CharacterSession?.State != KBot.App.Models.CharacterPresence.InGame)
                throw new Exception("O frame real do personagem no mapa não foi confirmado.");
            return;
        }

        if (args.Length == 2 && args[1] == "--vision-samples")
        {
            var detector = new VisionInGameDetector();
            var observed = new Dictionary<string, VisionEvidence>();
            foreach (var file in Directory.EnumerateFiles(args[0], "pokealliance-*.png").OrderBy(x => x))
            {
                var bitmap = new BitmapImage(new Uri(Path.GetFullPath(file)));
                var result = detector.Analyze(bitmap);
                observed[Path.GetFileName(file)] = result;
                Console.WriteLine($"{Path.GetFileName(file)}: {result.State}, {result.Confidence:P0}, {result.Status}");
                if (file.Contains("20260919-214211") &&
                    detector.Analyze(new TransformedBitmap(bitmap, new ScaleTransform(.6, .6))).State != KBot.App.Models.CharacterPresence.InGame)
                    throw new Exception("O detector falhou após redimensionar o frame InGame.");
            }
            var inGame = observed["pokealliance-20260919-214211.png"];
            var selection = observed["pokealliance-20260920-113945.png"];
            if (inGame.State != KBot.App.Models.CharacterPresence.InGame ||
                selection.State != KBot.App.Models.CharacterPresence.CharacterSelection)
                throw new Exception("Os frames reais de mapa/seleção foram classificados incorretamente.");
            var aggregator = new CharacterStateAggregator();
            var unavailable = new ClientStateEvidence(KBot.App.Models.CharacterPresence.Unknown, false, 0);
            aggregator.Update(unavailable, inGame);
            aggregator.Update(unavailable, inGame);
            if (aggregator.Update(unavailable, inGame).State != KBot.App.Models.CharacterPresence.InGame)
                throw new Exception("Três leituras de mapa não confirmaram InGame.");
            for (var i = 0; i < 3; i++)
                if (aggregator.Update(unavailable, selection).State != KBot.App.Models.CharacterPresence.InGame)
                    throw new Exception("A saída do mapa não aguardou quatro leituras.");
            var logout = aggregator.Update(unavailable, selection);
            if (logout.State != KBot.App.Models.CharacterPresence.CharacterSelection ||
                logout.DetectionStatus != KBot.App.Models.CharacterDetectionStatus.NotInGame)
                throw new Exception("O logout não removeu InGame após quatro leituras.");
            aggregator.Update(unavailable, inGame);
            aggregator.Update(unavailable, inGame);
            if (aggregator.Update(unavailable, inGame).State != KBot.App.Models.CharacterPresence.InGame)
                throw new Exception("O retorno ao mapa não reabriu InGame.");
            var conflictingReader = new ClientStateEvidence(KBot.App.Models.CharacterPresence.CharacterSelection, true, .99);
            for (var i = 0; i < 5; i++)
                if (aggregator.Update(conflictingReader, inGame).State != KBot.App.Models.CharacterPresence.InGame)
                    throw new Exception("Conflito com HUD visível foi contado como logout.");
            var noFrame = new VisionEvidence(KBot.App.Models.CharacterPresence.Unknown, 0, "No capture", 0, 5,
                null, Array.Empty<VisionRegion>(), VisionCaptureStatus.CaptureUnavailable);
            var lastConfirmed = aggregator.Update(unavailable, inGame).LastConfirmedInGame;
            for (var i = 0; i < 10; i++)
            {
                var held = aggregator.Update(unavailable, noFrame);
                if (held.State != KBot.App.Models.CharacterPresence.InGame ||
                    held.DetectionStatus != KBot.App.Models.CharacterDetectionStatus.CaptureUnavailable ||
                    held.LastConfirmedInGame != lastConfirmed)
                    throw new Exception("Captura indisponível alterou o InGame confirmado.");
            }
            for (var i = 0; i < 3; i++)
                if (aggregator.Update(unavailable, selection).State != KBot.App.Models.CharacterPresence.InGame)
                    throw new Exception("Logout ocorreu antes de quatro frames válidos.");
            if (aggregator.Update(unavailable, selection).State != KBot.App.Models.CharacterPresence.CharacterSelection)
                throw new Exception("Quatro frames válidos de seleção não confirmaram logout.");
            var invalidFrame = detector.Analyze(new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32));
            if (invalidFrame.CaptureStatus != VisionCaptureStatus.CaptureUnavailable)
                throw new Exception("Frame inválido não foi marcado como captura indisponível.");
            var blankFrame = detector.Analyze(new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32));
            if (blankFrame.CaptureStatus != VisionCaptureStatus.CaptureUnavailable)
                throw new Exception("Frame vazio não foi marcado como captura indisponível.");
            Console.WriteLine("Frames reais e estabilidade temporal: PASS.");
            return;
        }

        var app = new KBot.App.App();
        app.InitializeComponent();
        using var dashboardLifecycle = new KBotLifecycle();
        var window = new MainWindow(dashboardLifecycle);
        var viewModel = (MainViewModel)window.DataContext;

        using (var bootstrapLifecycle = new KBotLifecycle())
        {
            var bootstrap = new KBot.App.Views.BootstrapWindow(bootstrapLifecycle);
            Render(bootstrap, Path.Combine(args[0], "bootstrap.png"), 580, 600);
            bootstrap.Close();
        }
        var firstRun = new KBot.App.Views.ClientSelectorWindow(null, firstRun: true);
        Render(firstRun, Path.Combine(args[0], "configure-first-run.png"), 540, 300);
        firstRun.Close();

        Render(window, Path.Combine(args[0], "dashboard.png"));
        viewModel.NavigateCommand.Execute("CaveBot");
        Render(window, Path.Combine(args[0], "cavebot-empty.png"));
        var cavebot = (CavebotViewModel)viewModel.CavebotView.DataContext;
        foreach (var (name, x, y, z) in new[]
        {
            ("Entrada", "4066", "3458", "5"),
            ("Corredor", "4072", "3455", "5"),
            ("Retorno", "4075", "3451", "5")
        })
        {
            cavebot.NewName = name;
            cavebot.NewX = x;
            cavebot.NewY = y;
            cavebot.NewZ = z;
            cavebot.AddCommand.Execute(null);
        }
        Render(window, Path.Combine(args[0], "cavebot.png"));
        viewModel.NavigateCommand.Execute("Settings");
        var settings = (SettingsViewModel)viewModel.SettingsView.DataContext;
        settings.Monsters.Add("Pidgey");
        settings.Monsters.Add("Rattata");
        Render(window, Path.Combine(args[0], "settings.png"));
        viewModel.NavigateCommand.Execute("Target");
        if (!ReferenceEquals(settings, ((FrameworkElement)viewModel.CurrentView).DataContext))
            throw new Exception("O módulo Alvos não compartilha o perfil de Configurações.");
        Render(window, Path.Combine(args[0], "target.png"));
        foreach (var section in new[] { "Healing", "Catch", "Loot", "Fishing", "Alerts" })
        {
            viewModel.NavigateCommand.Execute(section);
            if (!ReferenceEquals(settings, ((FrameworkElement)viewModel.CurrentView).DataContext))
                throw new Exception($"O módulo {section} não compartilha o perfil de Configurações.");
            Render(window, Path.Combine(args[0], section.ToLowerInvariant() + ".png"));
        }

        if (args.Length == 2 && args[1] == "--core")
        {
            var probeWindow = new Window { Title = "KBot native attach smoke", Width = 200, Height = 100 };
            probeWindow.Show();
            var watcherClient = new KBot.App.Models.GameInstallation
            {
                Name = "Smoke",
                InstallDirectory = AppContext.BaseDirectory,
                LauncherPath = Path.Combine(AppContext.BaseDirectory, "KBot.Native.exe"),
                GameExecutableNames = new List<string> { "KBot.UiSmoke.exe" }
            };
            var detected = new GameProcessWatcher().FindRunning(watcherClient);
            if (detected?.Process.Id != Environment.ProcessId || detected.Value.Handle == 0)
                throw new Exception("O watcher não encontrou a janela do cliente configurado.");
            detected.Value.Process.Dispose();
            using var native = new NativeService();
            Console.WriteLine(native.StartCore());
            bool ping = false;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                ping = native.PingAsync().GetAwaiter().GetResult();
                if (ping) break;
                Thread.Sleep(100);
            }
            if (!ping)
                throw new Exception("O núcleo não respondeu ao PING.");
            KBot.App.Models.NativeStatus? status = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                status = native.GetStatusAsync(CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"Status: online={status?.NativeOnline}, process={status?.ProcessName}, error={native.LastStatusError}");
                if (status?.NativeOnline == true) break;
                Console.WriteLine($"Attempt {attempt + 1}: {native.LastStatusError}");
                Thread.Sleep(200);
            }
            if (status?.NativeOnline != true)
                throw new Exception("O aplicativo não conseguiu consultar o núcleo nativo.");
            if (!native.AttachGameAsync(Environment.ProcessId, "KBot.UiSmoke.exe", CancellationToken.None).GetAwaiter().GetResult())
                throw new Exception("O núcleo não vinculou o PID solicitado.");
            status = native.GetStatusAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (status?.Pid != Environment.ProcessId || status.Hwnd == 0)
                throw new Exception("O núcleo não retornou PID e HWND da janela vinculada.");
            if (status.ReaderStatus != "NOT_CONFIGURED" ||
                status.ReaderMessage?.Contains("AddressResolver", StringComparison.OrdinalIgnoreCase) != true)
                throw new Exception($"O motivo real do reader não foi publicado: {status.ReaderStatus} / {status.ReaderMessage}");
            native.DetachGameAsync(CancellationToken.None).GetAwaiter().GetResult();
            probeWindow.Close();
            Console.WriteLine($"Native core answered for {status.ProcessName}.");
        }

        if (args.Length == 2 && args[1] == "--watch-installed")
        {
            var installation = new GameLibrary().Load().First(c => c.Name == "PokeAlliance");
            var launcher = new GameLauncher().FindRunning(installation);
            var found = new GameProcessWatcher().FindRunning(installation, launcher);
            if (!found.HasValue) throw new Exception("O watcher não encontrou o jogo já aberto.");
            Console.WriteLine($"Existing client: PID={found.Value.Process.Id}, HWND=0x{found.Value.Handle.ToInt64():X}, executable={found.Value.Path}");
            found.Value.Process.Dispose();
        }

        if (args.Length == 2 && args[1] == "--lifecycle-installed")
        {
            using var lifecycle = new KBotLifecycle();
            lifecycle.RestartAsync().GetAwaiter().GetResult();
            Thread.Sleep(4000);
            if (lifecycle.GameSession?.Pid <= 0 || lifecycle.CharacterSession?.State != KBot.App.Models.CharacterPresence.Unknown ||
                lifecycle.CanShowMainWindow || lifecycle.State != KBot.App.Models.KBotLifecycleState.WaitingForCharacter)
                throw new Exception($"Estado inesperado: {lifecycle.State}, PID={lifecycle.GameSession?.Pid}, personagem={lifecycle.CharacterSession?.State}");
            Console.WriteLine($"Lifecycle: {lifecycle.State}, PID={lifecycle.GameSession!.Pid}, character={lifecycle.CharacterSession!.State}, dashboard locked.");
        }

        if (args.Length == 2 && args[1] == "--lifecycle-transition")
        {
            using var lifecycle = new KBotLifecycle(new SequenceDetector());
            var observed = new System.Collections.Concurrent.ConcurrentQueue<(KBot.App.Models.KBotLifecycleState State, bool CanShow)>();
            lifecycle.Changed += current => observed.Enqueue((current.State, current.CanShowMainWindow));
            lifecycle.RestartAsync().GetAwaiter().GetResult();
            Thread.Sleep(3500);
            var states = observed.ToArray();
            if (!states.Any(s => s.State == KBot.App.Models.KBotLifecycleState.Ready && s.CanShow) ||
                lifecycle.State != KBot.App.Models.KBotLifecycleState.WaitingForCharacter ||
                lifecycle.CanShowMainWindow || lifecycle.GameSession?.Pid <= 0)
                throw new Exception("A transição Ready → sem personagem não bloqueou a interface mantendo a GameSession.");
            Console.WriteLine("Lifecycle transitions: Ready → WaitingForCharacter; game session retained.");
        }

        Console.WriteLine("WPF views rendered successfully.");
    }

    private sealed class SequenceDetector : ICharacterSessionDetector
    {
        private int _calls;
        public CharacterDetection Detect(KBot.App.Models.GameSession game, KBot.App.Models.NativeStatus? status) =>
            new(Interlocked.Increment(ref _calls) == 1
                ? KBot.App.Models.CharacterPresence.InGame
                : KBot.App.Models.CharacterPresence.CharacterSelection,
                1, KBot.App.Models.CharacterDetectionSource.ClientReader,
                "READY", "Test", "Test", 0, 0, null, Array.Empty<VisionRegion>());
        public void Reset() => _calls = 0;
    }

    private static void Render(Window window, string path, int width = 1180, int height = 760)
    {
        var content = (FrameworkElement)window.Content;
        if (content is System.Windows.Controls.Panel panel) panel.Background = window.Background;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
