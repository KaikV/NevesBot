namespace KBot.App.BotBrain;

// ============================================================================
//  PESCA - pure cast gate (port of 0_AD_fish.lua cast loop)
// ---------------------------------------------------------------------------
//  The Lua macro(200) asks one question each tick: may I cast the rod right now?
//  Every answer is a short-circuit of independent, offset-free gates, which is
//  exactly what makes it unit-testable headless. In order (faithful to source):
//    1. settle after login            (boot+3000ms)
//    2. ping-aware cadence             (>= max(base, ping))
//    3. enough wilds already nearby?   (maxPoke pause, checked 1x/s with backoff)
//    4. catch/loot own this turn?      (cross-system busy flag)
//  The two gates that NEED a memory read - "is there a rod equipped" and "is a
//  fishing spot (waterId item) within reach" - are transport concerns. They arrive
//  as the rod/water reads; until then the caller treats a missing read as "cannot
//  cast" and degrades, matching the Lua's `if not rod then return end`.
// ============================================================================
public sealed class FishingGate
{
    // Settle after login: no casting in the first 3s of the session (Lua bootAt).
    public const long BootSettleMs = 3000;
    // Server answer rhythm default: the rod only gets a real response ~13s apart,
    // so casts faster than that are ignored - not faster, just wasted.
    public const int DefaultCastDelayMs = 13000;
    // Water search radius clamp (Lua raio 1..12).
    public const int MinRaio = 1;
    public const int MaxRaio = 12;
    // Max-poke pause is re-evaluated at most 1x/s (backoff): while paused the poll
    // would otherwise run every 200ms tick for nothing. Between polls the last
    // result stands.
    public const long PokeRecheckMs = 1000;

    private long _nextPokeCheckMs;
    private bool _paused;

    public bool Paused => _paused;

    // Decide, for this instant, whether the rod may be cast.
    //   nowMs        : wall clock now
    //   sessionStart : wall clock the session became online (for the settle gate)
    //   lastCastMs   : wall clock of the last cast (or <sessionStart = never)
    //   pingMs       : current round-trip (0 = unknown -> use base only)
    //   castDelayMs  : configured base cadence (Lua F.castDelay)
    //   maxPoke      : stop fishing when >= this many wilds are near (-1 = never)
    //   wildsNearby  : current live-wild count in range
    //   crossBusy    : catch or loot owns the turn right now (skip so we don't
    //                  fight them for the action)
    public bool ShouldCast(long nowMs, long sessionStart, long lastCastMs,
                           int pingMs, int castDelayMs, int maxPoke,
                           int wildsNearby, bool crossBusy)
    {
        if (nowMs < sessionStart + BootSettleMs) return false;
        if (nowMs - lastCastMs < System.Math.Max(castDelayMs, pingMs)) return false;

        bool pause = _paused;
        if (maxPoke >= 0 && nowMs >= _nextPokeCheckMs)
        {
            _nextPokeCheckMs = nowMs + PokeRecheckMs;
            pause = wildsNearby >= maxPoke;
        }
        _paused = pause;
        if (pause) return false;

        return !crossBusy;
    }

    // Clamp a configured water search radius into [1,12] (Lua raio guard).
    public static int ClampRaio(int raio) => System.Math.Clamp(raio, MinRaio, MaxRaio);
}
