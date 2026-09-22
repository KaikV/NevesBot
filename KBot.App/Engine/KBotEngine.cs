using KBot.App.BotBrain;
using KBot.App.Engine.Actions;
using KBot.App.Engine.Events;
using KBot.App.Engine.Fusion;
using KBot.App.Engine.Runtime;
using KBot.App.Engine.Scheduler;
using KBot.App.Engine.Sensors;
using KBot.App.Engine.State;
using KBot.App.Models;
using KBot.App.Services;

namespace KBot.App
{
    /// <summary>
    /// The closed loop of the hybrid engine:
    ///   sensors (multi-rate) -> latest samples -> Fusion -> Delta/EventBus
    ///   -> Confirmation of the in-flight action -> ALL modules decide
    ///   -> ActionManager arbitrates ONE winner -> Executor fires honestly
    ///   -> confirmation next tick decides retry/abort/confirm.
    ///
    /// It owns NO game logic (modules do), NO transport specifics (sensors do), and NO
    /// confirmation policy (Confirmation does). This class only sequences them and keeps the
    /// observable state (Log / status) for the dashboard. Headless-testable: everything heavy
    /// arrives as delegates/instances, nothing here touches WPF.
    /// </summary>
    public sealed class KBotEngineConfig
    {
        /// Reads the latest structured client status (native pipe). May return null = core down.
        public required Func<Task<NativeStatus?>> ReadNative { get; init; }
        /// Current game window handle (0 when none).
        public required Func<nint> Hwnd { get; init; }
        /// Presence probe over the EXISTING detection result (no re-implementation).
        public required Func<PresenceProbe> Presence { get; init; }
        /// Initial profile; the lifecycle pushes updates via RefreshProfile.
        public required BotProfile Profile { get; init; }
        public int FastMs { get; init; } = 100;
        public int NormalMs { get; init; } = 300;
        public int SlowMs { get; init; } = 1500;
        public int MaxRetries { get; init; } = 2;
        /// Frame capture for the visual sensors. Null (the default on Windows) builds the real
        /// <see cref="GameWindowFrameSource"/> lazily; tests inject a synthetic source and stay WPF-free.
        public Func<IFrameSource>? Frames { get; init; }
    }

    public sealed class KBotEngine : IDisposable
    {
        private SensorSampleStore _store;
        /// <summary>Latest-samples store. Exposed (reference type) so headless tests can prime/poll sensors directly.</summary>
        public SensorSampleStore Store => _store;
        private readonly SensorScheduler _scheduler;
        private readonly DeltaEngine _delta = new();
        private readonly EventBus _bus = new();
        private readonly ActionManager _manager;
        private readonly ActionExecutor _executor;
        private readonly List<IBotModule> _modules;
        private readonly IProfileView _profile;
        private readonly List<GameDeltaType> _loggedDeltas = new();

        public BotRuntimeState Runtime { get; } = new();

        private string _log = "Engine parado.";
        private bool _started;

        // Dashboard surface (mirrors what the legacy BotBrain exposed).
        public string Log => _log;
        public string StatusLine => Runtime.StatusLine;
        public bool Started => _started;

        // "pos ok · criaturas ok · cliente online" - the honest per-signal health for the UI card.
        public string Signals { get; private set; } = "";

        public event Action? Changed;

        public KBotEngine(KBotEngineConfig config, IEnumerable<IBotModule> modules, IKeySender keys)
        {
            var frame = config.Frames?.Invoke() ?? new NoFrameSource();
            var registrations = new List<SensorRegistration>
            {
                SensorRegistration.From(new StructuredPositionSensor(config.ReadNative)),
                SensorRegistration.From(new VisionPresenceSensor(config.Presence)),
                SensorRegistration.From(new VisualCreatureSensor(frame)),
            };
            var store = new SensorSampleStore(registrations);
            _store = store;
            _scheduler = new SensorScheduler(registrations, store, null, config.FastMs, config.NormalMs, config.SlowMs);
            _scheduler.Error += OnSensorError;

            _manager = new ActionManager(config.MaxRetries);
            _executor = new ActionExecutor(keys);
            var view = new ProfileView(config.Profile);
            _profile = view;
            _executor.SetProfile(view);
            _modules = modules.OrderByDescending(m => m.Priority).ToList();

            // Observability only: record the last N distinct delta descriptions for the UI.
            _bus.Subscribe(d =>
            {
                _loggedDeltas.Add(d.Type);
                if (_loggedDeltas.Count > 8) _loggedDeltas.RemoveAt(0);
            });
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            _scheduler.Start();
            _delta.Reset();
            Changed?.Invoke();
        }

        public void RefreshProfile(BotProfile profile) => _profile.Refresh(profile);

        public IReadOnlyList<IBotModule> Modules => _modules;

        /// <summary>
        /// One engine tick (the lifecycle calls this ~1/s while Ready). Order matters and is the
        /// whole point: we CONFIRM what is in flight before any new decision, so the bot never
        /// acts twice at once and never assumes success.
        /// </summary>
        public void Tick()
        {
            if (!_started) return;
            var now = Environment.TickCount64;

            // 1) Fuse the latest sensor samples into THIS tick's world truth (read-only, never blocks).
            var fusion = new SensorFusion(now);
            var snapshot = fusion.Fuse(_store);
            SetSignals(snapshot);

            // 2) Edge detection on trustworthy transitions; publish to interested subscribers.
            //    The most impactful edge of THIS tick is remembered so step 5 keeps it visible in the
            //    single-line log instead of clobbering it with "nada a fazer".
            AutomationEventSeverity? worldSev = null;
            string? worldEvent = null;
            foreach (var d in _delta.Detect(snapshot))
            {
                _bus.Publish(d);
                var sev = WorldDeltaSeverity(d.Type);
                if (!worldSev.HasValue || sev > worldSev.Value) { worldSev = sev; worldEvent = d.Describe(); }
                PublishWorldDelta(d);
            }

            // 3) Resolve whatever action is in flight against the FRESH world (honesty step).
            //    Remember its outcome: it outranks an idle line but not a fired-action line.
            string? confirmLine = null;
            ResolveInFlight(snapshot, now, out confirmLine);

            // 4) Every module gets its say; the arbiter picks the single winner.
            var legacy = snapshot.ToLegacy();
            var intents = new List<IntentV2>();
            foreach (var module in _modules)
            {
                ActionIntent? intent;
                try { intent = module.Decide(legacy, _profile); }
                catch (Exception ex)
                {
                    AutomationEventHub.Shared.Publish(AutomationEventSeverity.Error, module.Name,
                        "module_failed", ex.Message, module.Name);
                    continue;
                }
                if (intent is not null)
                    intents.Add(IntentBridge.FromLegacy(intent, module.Name, now));
            }

            var stepLog = _log;
            var res = _manager.Decide(snapshot, Runtime, intents, now);
            if (!res.HasWinner || res.Winner is not { } winner)
            {
                // Nothing fired: the most important thing that happened THIS tick wins the single line -
                // a confirm/abort of the previous action beats a world edge, which beats "idle".
                if (_log != stepLog) return;
                if (confirmLine is { } c) { SetLog(c); return; }
                if (worldEvent is { } w) { SetLog($"[mundo] {w}"); return; }
                SetLog(intents.Count == 0 ? "nada a fazer" : $"aguardando: {res.Reason}");
                return;
            }

            // 5) Fire the winner through the REAL hardware path and report the honest outcome.
            var fired = _executor.Execute(winner, snapshot);
            Runtime.StatusLine = $"{winner.SourceModule}: {winner}";
            SetLog($"{winner.SourceModule}: {winner.Detail ?? winner.Payload} ({fired.Outcome})");
            AutomationEventHub.Shared.Publish(
                fired.Accepted ? AutomationEventSeverity.Info : fired.Outcome == FireOutcome.Pending
                    ? AutomationEventSeverity.Warning : AutomationEventSeverity.Warning,
                winner.SourceModule,
                fired.Accepted ? "action_fired" : fired.Outcome == FireOutcome.Pending ? "action_pending" : "action_rejected",
                $"{winner} -> {fired.Outcome}: {fired.Detail}", winner.SourceModule, TimeSpan.FromSeconds(1));
        }

        // --- the loop's honesty step -----------------------------------------------------------

        private void ResolveInFlight(GameStateSnapshot snapshot, long now, out string? line)
        {
            line = null;
            var pending = Runtime.PendingAction;
            if (pending is null || pending.Kind == ActionKind.None) return;

            var verdict = Confirmation.Check(pending, snapshot, now);
            switch (verdict.State)
            {
                case ConfirmState.Confirmed:
                    _manager.Confirm(Runtime, now);
                    line = $"confirmado: {verdict.Detail}";
                    break;
                case ConfirmState.NotYet:
                    if (!_manager.Fail(Runtime, now))
                        line = $"desistiu apos retries: {verdict.Detail}";
                    break;
                case ConfirmState.Unverifiable:
                    // No sensor can prove this effect today: release WITHOUT claiming success.
                    _manager.Abort(Runtime, now);
                    line = $"liberado sem verificacao: {verdict.Detail}";
                    break;
            }
        }

        // World deltas (client lost, poke fainted, wilds appeared...) are operator-critical signals, so
        // they go to the shared alert feed too - not just the one-line bot log. Severity follows impact;
        // dedupe + capacity come from the hub itself, so a flickering edge cannot flood the UI.
        private static void PublishWorldDelta(GameDelta d) =>
            AutomationEventHub.Shared.Publish(WorldDeltaSeverity(d.Type), "Mundo", WorldDeltaCode(d), d.Describe());

        private static AutomationEventSeverity WorldDeltaSeverity(GameDeltaType t) => t switch
        {
            GameDeltaType.ClientLost or GameDeltaType.PokeFainted => AutomationEventSeverity.Error,
            GameDeltaType.LeftGame or GameDeltaType.PositionLost or GameDeltaType.WildsAppeared
                => AutomationEventSeverity.Warning,
            _ => AutomationEventSeverity.Info,
        };

        // "ClientLost" -> "client_lost": consistent with the rest of the hub's event codes.
        private static string WorldDeltaCode(GameDelta d)
        {
            var name = d.Type.ToString();
            var sb = new System.Text.StringBuilder(name.Length + 4);
            foreach (var c in name)
                if (char.IsUpper(c))
                {
                    if (sb.Length > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            return sb.ToString();
        }

        private void OnSensorError(string name, Exception ex) =>
            AutomationEventHub.Shared.Publish(AutomationEventSeverity.Warning, "Sensors", "sensor_error",
                $"{name}: {ex.Message}", name, TimeSpan.FromSeconds(2));

        private void SetSignals(GameStateSnapshot s)
        {
            var pos = s.HasPosition ? "pos ok" : "pos ?";
            var cre = s.CreaturesRead ? $"criaturas ok ({s.Wilds.Count})" : "criaturas ?";
            var client = s.ClientOnline ? "cliente online" : "cliente offline";
            var value = $"{pos} · {cre} · {client}";
            if (Signals == value) return;
            Signals = value;
            Changed?.Invoke();
        }

        private void SetLog(string value)
        {
            if (_log == value) return;
            _log = value;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            _started = false;
            _ = _scheduler.DisposeAsync().AsTask();
            Changed?.Invoke();
        }

        // Used when no capture source was injected (should not happen on Windows - the lifecycle always
        // provides one). Reports "unavailable" so the visual sensors read Unavailable, never an empty world.
        private sealed class NoFrameSource : IFrameSource
        {
            public CaptureResult Capture() => new(CaptureOutcome.Unavailable, null, "sem fonte de captura injetada");
        }
    }
}
