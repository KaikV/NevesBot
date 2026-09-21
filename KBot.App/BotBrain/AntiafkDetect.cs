namespace KBot.App.BotBrain;

// A game-space tile coordinate (game coords, not screen pixels).
public readonly record struct PokePos(int X, int Y, int Z);

// ============================================================================
//  ANTI AFK  (port of nL_antiafk.lua)
// ---------------------------------------------------------------------------
//  Pure state machine. When the character stands on the SAME tile for a
//  configured number of seconds (min 15 - less looks like trembling, not people),
//  it takes ONE step sideways and immediately schedules the step back, so the
//  character never migrates. Sides alternate (right/left). It NEVER steps while
//  another feature owns the bot (combo/lure/market brake), and never into a
//  blocked tile - if both sides are blocked it retries later instead of pushing
//  against a wall. It never spins in place: spinning while idle is THE most
//  bot-revealing move in the game, and it doesn't change position anyway.
// ============================================================================
public sealed class AntiAfkTracker
{
    // Floor below this the character just trembles; 15s still looks human.
    public const int IdleMinSeconds = 15;
    // How long after the out-step the return step is scheduled.
    public const long VoltaMs = 1500;

    private PokePos? _ult;
    private long _paradoAt = -1;
    private string? _voltarDir;
    private long _voltarAt;
    private bool _rightSide = true;   // preferred side for the NEXT out-step (starts right, then alternates)
    public int Steps { get; private set; }
    public string Diag { get; private set; } = "";

    /// One anti-AFK decision. Returns "RIGHT"/"LEFT" (a single tile step to send
    /// now) or null. The return step has priority: a character must not be left
    /// standing on the side tile. Unknown walkability (null) degrades to "free".
    public string? Tick(long nowMs, bool online, PokePos? pos,
                        bool busy, bool? eastOk, bool? westOk, int idleSeconds)
    {
        if (!online)
        {
            _ult = null;
            _paradoAt = -1;
            _voltarDir = null;
            Diag = "";
            return null;
        }

        // The RETURN always wins: schedule check before reading anything else.
        if (_voltarDir != null && nowMs >= _voltarAt)
        {
            var d = _voltarDir;
            _voltarDir = null;
            _ult = null;        // we moved ourselves: restart the idle clock
            _paradoAt = -1;
            return d;
        }

        if (pos is not { } p) return null;

        // Moved (by the player, the cavebot, or us): not AFK, zero the clock.
        if (_ult is null || _ult.Value != p)
        {
            _ult = p;
            _paradoAt = nowMs;
            return null;
        }

        // Another feature mid-action: the clock keeps running but the step waits.
        // Walking now would close the market / break the combo build-up.
        if (busy) return null;

        var idle = System.Math.Max(IdleMinSeconds, idleSeconds) * 1000L;
        if (_paradoAt >= 0 && nowMs - _paradoAt < idle) return null;

        bool rightFree = eastOk ?? true;
        bool leftFree = westOk ?? true;
        bool goRight = _rightSide;
        if (goRight && !rightFree) goRight = false;
        if (!goRight && !leftFree) goRight = true;
        if ((goRight && !rightFree) || (!goRight && !leftFree))
        {
            // Walled on both sides: don't push into the wall, retry later. The attempt
            // still consumes its side (the character tried) so the pattern keeps alternating.
            _rightSide = !goRight;
            Steps++;
            _paradoAt = nowMs;
            Diag = "sem tile livre ao lado";
            return null;
        }

        _voltarDir = goRight ? "LEFT" : "RIGHT";
        _voltarAt = nowMs + VoltaMs;
        _rightSide = !goRight;     // alternate the side on the next trigger
        Steps++;
        _paradoAt = nowMs;         // restart the idle clock at our new (side) tile
        Diag = "";
        return goRight ? "RIGHT" : "LEFT";
    }
}
