namespace KBot.App.BotBrain;

// One configured "catch this corpse with this ball" line from the player's list
// (0_AB_catch.lua C.entries): an enabled flag, the corpse item id to watch, and the
// ball item id to throw. A ball id under 100 means "no real ball bound" and must be
// treated as missing - that line looks configured but never throws, so the bot would
// sit on the corpse forever if we pressed it blind.
public sealed record CatchEntry(string Name, int CorpseId, int BallId)
{
    public bool HasBall => BallId >= 100;
}

// The outcome of one corpse evaluation: which ball (if any) to throw.
public readonly record struct CatchDecision(int BallId, string Reason)
{
    public bool Throw => BallId >= 100;
    public override string ToString() => Throw ? $"bola {BallId} ({Reason})" : Reason;
}

// Pure port of the BALL-SELECTION priority in 0_AB_catch.lua handleCorpse. It takes
// everything it needs as arguments (the corpse that dropped, where I last killed
// something, my shiny words, the ball bindings) and returns which ball to throw. No
// IO, no clock of its own (the timestamps are passed in as "how long ago"), so it is
// fully unit-testable headless. The transport (corpse scan + onCreatureDisappear) and
// the queue/wait logic live elsewhere.
public static class CatchSelection
{
    // How long a "my kill / shiny died here" mark stays valid for (Lua MINHA_MORTE_MS
    // and SHINY_DEATH_WINDOW_MS). A corpse with no fresh MY-death behind it is another
    // player's kill -> no ball, otherwise the server rejects and the corpse stays.
    public const long MyDeathWindowMs = 8000;
    public const long ShinyDeathWindowMs = 12000;

    // Evaluate the corpse that just dropped at our position.
    //   corpseId          : the item id that appeared on the tile
    //   myKilledHereMs    : how long ago one of OUR creatures died at this tile
    //                       (-1 = nothing of ours died here)
    //   shinyDiedHereMs   : how long ago a SHINY of ours died at this tile (-1 = none)
    //   entries           : the player's configured corpse->ball lines
    //   customEnabled     : master toggle for those lines
    //   shinyEnabled      : master toggle for shiny catching
    //   shinyBall         : the shiny ball binding (0 = none bound)
    //   shinyWords        : substrings that mark a name as shiny (e.g. "shiny")
    public static CatchDecision Evaluate(
        int corpseId,
        string corpseName,
        long myKilledHereMs,
        long shinyDiedHereMs,
        IReadOnlyList<CatchEntry> entries,
        bool customEnabled,
        bool shinyEnabled,
        int shinyBall,
        IReadOnlyList<string> shinyWords)
    {
        // 1) Explicit per-pokemon line wins: the exact corpse id the player mapped.
        //    We only throw where WE killed (myKilledHereMs fresh) and only if that line
        //    actually has a real ball bound.
        if (customEnabled)
        {
            foreach (var e in entries)
            {
                if (e.CorpseId != corpseId) continue;
                bool iKilledIt = myKilledHereMs is >= 0 and <= MyDeathWindowMs;
                if (!iKilledIt)
                    return new CatchDecision(0, "corpo de outro jogador");
                if (!e.HasBall)
                    return new CatchDecision(0, $"linha sem pokebola ({e.Name})");
                return new CatchDecision(e.BallId, e.Name);
            }
        }

        if (!shinyEnabled || shinyBall < 100)
            return new CatchDecision(0, "shiny desligado ou sem bola");

        // 2) Shiny by NAME: one of OUR shinies died at this tile within the window -> the
        //    corpse here is its, any id. Recognized by the name prefix (this server
        //    prefixes the display name rather than tagging the corpse id).
        if (IsShinyName(corpseName, shinyWords)
            && shinyDiedHereMs is >= 0 and <= ShinyDeathWindowMs)
            return new CatchDecision(shinyBall, "shiny pelo nome");

        // 3) Shiny by CORPSE ID range is server-specific and unreliable here, so the
        //    name path above is what actually fires. We still gate on a fresh MY-death.
        return new CatchDecision(0, "sem regra que casa");
    }

    // Case-insensitive substring match against the whole display name - main.lua does
    // exactly this so any server prefix ("Shiny Mawile [169]") works.
    private static bool IsShinyName(string name, IReadOnlyList<string> words)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.ToLowerInvariant();
        foreach (var raw in words)
        {
            var kw = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (kw.Length > 0 && n.Contains(kw, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
