namespace KBot.App.BotBrain;

// Port of 0_AB_catch.lua. Priority 80 (after combat, before route).
// A fresh corpse on our tile is evaluated by CatchSelection (explicit per-pokemon
// line first, then shiny-by-name). Without a corpse signal yet (vision transport),
// it degrades to the old rule: enabled + in battle -> press the ball binding.
public sealed class CatchModule : IBotModule
{
    public string Name => "Catch";
    public int Priority => 80;

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!p.CatchEnabled) return null;

        if (s.CorpseId is int corpseId)
        {
            // The corpse appeared on our tile at CorpseAppearedMs; that age is the
            // only "how long ago" signal we have today (there is no separate death
            // log transport), so both the my-kill and shiny-death windows share it.
            long age = s.NowMs - s.CorpseAppearedMs;
            if (age < 0) age = 0;
            var d = CatchSelection.Evaluate(
                corpseId, s.CorpseName ?? string.Empty, age, age,
                p.CatchEntries, customEnabled: true,
                shinyEnabled: p.CatchShinyEnabled, shinyBall: p.ShinyBallId,
                shinyWords: p.RareWords);
            return d.Throw
                ? ActionIntent.Command("catch", $"bola:{d.BallId}:{d.Reason}")
                : null;
        }

        // No corpse read this tick -> fall back to the battle-time binding.
        if (s.InBattle == true) return ActionIntent.Command("catch");
        return null;
    }
}

// Port of the loot handling in main.lua. Priority 70.
public sealed class LootModule : IBotModule
{
    public string Name => "Loot";
    public int Priority => 70;

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!p.LootEnabled) return null;
        return s.InBattle != true ? ActionIntent.Command("loot") : null;
    }
}

// Port of n3_alarmes.lua (the engine behind SAIU / SUPRIMENTO / MORTE / NIVEL /
// CAPTUROU). Priority 60. The edge detectors (levelup, death, playerout, supply)
// live in AlertDetect.cs as pure state machines; this module feeds them per-tick
// from GameState and emits an "alert:<type>" command when any edge fires. The
// Telegram delivery belongs to the app layer, not the brain.
public sealed class AlertsModule : IBotModule
{
    public string Name => "Alertas";
    public int Priority => 60;

    private readonly LevelUpTracker _lvl = new();
    private readonly DeathTracker _death = new();
    private readonly PresenceTracker _presence = new();
    private readonly Dictionary<string, SupplyTracker> _supply = new();
    private readonly CatchFeed _catchFeed = new();
    // Cooldown between alert commands so one tick doesn't chain-fire.
    // -1 = never fired; the first tick can always alert (avoiding overflow with NowMs=0).
    private long _lastAlertMs = -1;
    private const long AlertCooldownMs = 4000;  // matches Lua 4s rule cooldown

    public string Status { get; private set; } = "";

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        // Every alert obeys the same 4s cooldown (sofaAL rule cooldown) so one bad
        // tick can't chain-fire. The edge detectors below only re-arm on transition,
        // so the cooldown is backstop against overlapping edges, not the trigger.
        if (!CanFire(s)) return null;

        // Legacy signal: player got pulled (dragged by move/trap).
        if (s.Pulled) return Fire(s, "alert:pulled");

        if (!p.AlertsEnabled) return null;

        // --- Death (MORTE) ---------------------------------------------------
        // ActiveAlive=false is our proxy for "the character is down". A true
        // character-death offset (player hp) will tighten this later; the edge
        // logic + re-arm is identical regardless of the source.
        if (_death.Fire(s.ActiveAlive))
            return Fire(s, "alert:morte:o personagem morreu");

        // --- Level up (SUBIU DE NIVEL) ---------------------------------------
        if (_lvl.Tick(s.CharacterLevel))
            return Fire(s, $"alert:nivel:subiu para o nivel {s.CharacterLevel}");

        // --- Player out (SAIU DA TELA) ---------------------------------------
        var whoLeft = _presence.Tick(
            s.OtherPlayerPresent ?? false, s.OtherPlayerName);
        if (whoLeft != null)
            return Fire(s, $"alert:saiu:jogador saiu da tela: {whoLeft}");

        // --- Supply (SUPRIMENTO ACABANDO) ------------------------------------
        if (p.SupplyAlerts.Count > 0)
        {
            foreach (var (key, min) in p.SupplyAlerts)
            {
                int? count = null;
                if (s.SupplyCounts != null && s.SupplyCounts.TryGetValue(key, out var c))
                    count = c;
                if (!_supply.TryGetValue(key, out var tr))
                {
                    tr = new SupplyTracker();
                    _supply[key] = tr;
                }
                var msg = tr.Tick(count, min);
                if (msg != null)
                    return Fire(s, $"alert:suprimento:{msg}");
            }
        }

        // --- Caught (CAPTURAR) -----------------------------------------------
        // The HONEST proof is the server chat line, not a disappearing corpse.
        if (s.LatestChat is { } chat)
        {
            var cp = _catchFeed.Offer(chat.Src, chat.Sender, chat.Text, chat.AtMs);
            if (cp.IsCatch)
            {
                var nome = string.IsNullOrEmpty(cp.Name) ? "um pokemon" : cp.Name;
                var pre = (cp.Shiny && !nome.ToLowerInvariant().Contains("shiny")) ? "SHINY " : "";
                return Fire(s, $"alert:capturou:{pre}{nome}");
            }
        }

        return null;
    }

    private ActionIntent Fire(GameState s, string payload)
    {
        _lastAlertMs = s.NowMs;
        Status = payload;
        return ActionIntent.Command(payload);
    }

    private bool CanFire(GameState s) => _lastAlertMs < 0 || s.NowMs - _lastAlertMs >= AlertCooldownMs;
}

// Port of the fishing panel (0_AD_fish.lua). Priority 55 (after alerts, before route):
// when fishing is enabled, in-game and not in battle, cast the rod at the
// server's own rhythm (default ~13s - casting faster does not catch faster).
// The cast gate (settle-after-login, cadence, max-poke pause) is pure logic in
// FishingGate.cs; the rod-equipped / water-tile reads are transport concerns and
// degrade to "cannot cast" until they land.
public sealed class FishingModule : IBotModule
{
    public string Name => "Pesca";
    public int Priority => 55;

    private readonly FishingGate _gate = new();
    private long _lastCastMs = long.MinValue;
    private long? _sessionStartMs;   // first tick we saw in-game (settle gate anchor)
    public string Status { get; private set; } = "";

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!p.FishingEnabled || s.InBattle == true) { _sessionStartMs = null; return null; }

        // Anchor the settle clock to the first in-game tick of this session.
        _sessionStartMs ??= s.NowMs;
        long sessionStart = _sessionStartMs.Value;

        int baseMs = p.FishingDelaySeconds > 0 ? p.FishingDelaySeconds * 1000 : FishingGate.DefaultCastDelayMs;
        bool cast = _gate.ShouldCast(
            s.NowMs, sessionStart,
            lastCastMs: _lastCastMs < sessionStart ? sessionStart - FishingGate.DefaultCastDelayMs : _lastCastMs,
            pingMs: 0,                       // native ping read not wired yet -> use base cadence
            castDelayMs: baseMs,
            maxPoke: p.FishingMaxPoke,
            wildsNearby: s.WildsNearby,
            crossBusy: false);               // catch/loot busy flag arrives via GameState when wired

        Status = cast ? "lançando vara" : (_gate.Paused ? "pausado (pokes perto)" : "aguardando");
        if (!cast) return null;
        _lastCastMs = s.NowMs;
        return ActionIntent.Command("fish");
    }
}

// Port of nL_antiafk.lua. Lowest priority: a single sideways step when the
// character stands on one tile for too long, followed by the scheduled step back.
// The idle clock, volta scheduling, side alternation and both-sides-blocked retry
// live in AntiafkTracker (AntiafkDetect.cs). Walkability of the side tiles is not
// readable yet (null = "free", matching the Lua default), and "busy" stays false
// until a cross-feature hold flag lands.
public sealed class AntiAfkModule : IBotModule
{
    public string Name => "AntiAfk";
    public int Priority => 1;

    private readonly AntiAfkTracker _tracker = new();

    public string Status { get; private set; } = "";

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!p.AntiAfkEnabled || !s.InGame || !s.HasPosition || s.InBattle == true)
            return null;

        var dir = _tracker.Tick(
            s.NowMs, online: true,
            pos: new PokePos(s.X, s.Y, s.Z),
            busy: false,
            eastOk: null, westOk: null,
            idleSeconds: p.AntiAfkIdleSeconds);

        if (dir != null) return ActionIntent.Move(dir);
        return null;
    }
}
