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

// Port of n3_alarmes.lua core signal (pulled/GM). Priority 60 -> surfaces an
// alert command; Telegram delivery belongs to the app layer, not the brain.
public sealed class AlertsModule : IBotModule
{
    public string Name => "Alertas";
    public int Priority => 60;

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (s.Pulled) return ActionIntent.Command("alert", "pulled");
        return null;
    }
}

// Port of the fishing panel. Priority 55 (after alerts, before route): when
// fishing is enabled, we are in game and not in battle, cast the fishing hook.
public sealed class FishingModule : IBotModule
{
    public string Name => "Pesca";
    public int Priority => 55;

    private long _lastCastMs;

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!p.FishingEnabled || s.InBattle == true) return null;
        if (s.NowMs - _lastCastMs < 4000) return null; // one hook every ~4s
        _lastCastMs = s.NowMs;
        return ActionIntent.Command("fish");
    }
}

// Port of nL_antiafk.lua. Lowest priority: a tiny periodic nudge so the session
// never AFK-outs. Emits a move only when idle and far from any other activity.
public sealed class AntiAfkModule : IBotModule
{
    public string Name => "AntiAfk";
    public int Priority => 1;

    private long _lastNudgeMs;
    private bool _nudgeLeftNext = true;

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!s.InGame || s.InBattle == true) return null;
        // Only nudge once a minute, and skip while route/combat clearly owns moves.
        if (s.NowMs - _lastNudgeMs < 60_000) return null;
        _lastNudgeMs = s.NowMs;
        _nudgeLeftNext = !_nudgeLeftNext;
        return ActionIntent.Move(_nudgeLeftNext ? "LEFT" : "RIGHT");
    }
}
