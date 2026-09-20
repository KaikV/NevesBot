namespace KBot.App.BotBrain;

// Port of 0_AB_catch.lua / panels_looting.lua. Priority 80 (after combat, before route).
// Today: if catch is enabled and we are in battle, request a ball via its binding.
// The "should I throw" decision (species list, shiny check n7) needs battle state;
// it degrades to "in battle + enabled" until that offset/vision exists.
public sealed class CatchModule : IBotModule
{
    public string Name => "Catch";
    public int Priority => 80;

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!p.CatchEnabled) return null;
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
