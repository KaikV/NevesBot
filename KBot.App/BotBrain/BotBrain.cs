using KBot.App.Models;
using KBot.App.Services;

namespace KBot.App.BotBrain;

// The orchestrator: owns the module list and the tick loop. Each tick it builds
// a GameState snapshot, walks modules high-to-low priority, and realises the
// first intent any module asks for. It never decides game logic itself - that
// lives in the modules (the ported Kryon features).
public sealed class BotBrain : IDisposable
{
    private readonly List<IBotModule> _modules = new();
    private readonly IActionSink _sink;
    private readonly Func<GameState> _stateProvider;
    private readonly IProfileView _profile;

    public string Log { get; private set; } = "Brain parado.";
    public bool IsRunning { get; private set; }
    public IReadOnlyList<IBotModule> Modules => _modules;

    // Which signals are actually being read vs. still blocked on an offset/vision.
    // Surfaced by the dashboard so it's obvious what is gating each module.
    public string SignalSummary { get; private set; } = "";

    public event Action? Changed;

    // Forwarded from the sink so the app can see which offset-bound action is waiting.
    public event Action<ActionIntent>? CommandRequested
    {
        add => (_sink as BrainActionSink)?.CommandRequested += value;
        remove => (_sink as BrainActionSink)?.CommandRequested -= value;
    }
    public string? PendingCommand => (_sink as BrainActionSink)?.PendingCommand;

    public BotBrain(IActionSink sink, Func<GameState> stateProvider, IProfileView profile)
    {
        _sink = sink;
        _stateProvider = stateProvider;
        _profile = profile;
    }

    public void RefreshProfile(BotProfile profile) => _profile.Refresh(profile);

    public void Register(params IBotModule[] modules)
    {
        _modules.AddRange(modules);
        var ordered = _modules.OrderByDescending(m => m.Priority).ToList();
        _modules.Clear();
        _modules.AddRange(ordered);
        Changed?.Invoke();
    }

    // One decision step. Returns the intent that was realised (null if nothing
    // wanted to act). Safe to call from the lifecycle loop once per second.
    public ActionIntent? Tick()
    {
        var state = _stateProvider();
        SetSignals(state);
        if (!state.InGame)
        {
            SetLog("Fora de jogo / sem personagem - brain idle.");
            return null;
        }

        foreach (var module in _modules)
        {
            ActionIntent? intent;
            try { intent = module.Decide(state, _profile); }
            catch (Exception ex)
            {
                AutomationEventHub.Shared.Publish(AutomationEventSeverity.Error, module.Name,
                    "module_failed", ex.Message, module.Name);
                continue; // a bad module must not stall the loop
            }

            if (intent is null) continue;

            if (_sink.Execute(intent, state))
            {
                SetLog($"{module.Name}: {intent}");
                AutomationEventHub.Shared.Publish(AutomationEventSeverity.Info, module.Name,
                    "action_executed", Log, module.Name, TimeSpan.FromSeconds(1));
                return intent;
            }
            AutomationEventHub.Shared.Publish(AutomationEventSeverity.Warning, module.Name,
                "action_rejected", $"Ação recusada pelo canal: {intent}", module.Name);
            // sink refused -> fall through so a lower-priority module can act
        }
        return null;
    }

    private void SetSignals(GameState s)
    {
        var parts = new List<string> { s.HasPosition ? "pos ok" : "pos ?" };
        parts.Add(s.InBattle != null ? $"batalha {s.InBattle}" : "batalha ?");
        parts.Add(s.ActiveHpPercent is { } hp ? $"hp {(int)hp}%" : "hp ?");
        parts.Add(s.ActiveAlive != null ? $"ativo {s.ActiveAlive}" : "ativo ?");
        var value = string.Join(" · ", parts);
        if (SignalSummary == value) return;
        SignalSummary = value;
        Changed?.Invoke();
    }

    private void SetLog(string value)
    {
        if (Log == value) return;
        Log = value;
        Changed?.Invoke();
    }

    public void Dispose() { IsRunning = false; Changed?.Invoke(); }
}
