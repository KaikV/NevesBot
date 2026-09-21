namespace KBot.App.BotBrain;

// One visible creature from the map scan (g_map.getSpectatorsInRange / getSpectators).
// This is what the native/vision layer will eventually feed us; today tests inject it
// directly. The pokebar is deliberately ABSENT here: this snapshot is the "honest"
// source the bot trusts when the local pokebar copy lies.
public sealed record ScannedCreature(int Type, double HealthPercent, int X, int Y, int Z, string Name = "")
{
    // PokeAlliance marks creatures by TYPE, not skull (main.lua:938):
    public const int SummonOwn = 3;   // YOUR pokemon  -> never target
    public const int SummonOther = 4; // other players -> never target

    public bool IsMonster => Type != SummonOwn && Type != SummonOther;
    public bool IsAlive => HealthPercent > 0;
}

// Result of one screen pass. Solves BOTH questions the bot asks every tick
// ("which wilds are on screen" and "where is MY poke") in one loop - exactly the
// _cbScreen() optimization (before, they each walked the creature list).
public sealed record ScanResult(bool Read, IReadOnlyList<ScannedCreature> Wilds, ScannedCreature? MyPoke)
{
    // Read=false: the scan itself did not happen (no transport yet / offline).
    // Callers must treat that as UNKNOWN, not as "empty screen".
    public bool HasRead => Read;
    // sofaPokeOut() distilled: a live poke on the field? A corpse (hp<=0) drawn a
    // beat before the server removes it does NOT count - counting it held socorro
    // exactly when it was needed (real log 26/08).
    public bool HasPokeOnField => MyPoke is not null && MyPoke.IsAlive;
}

// Pure port of the screen-vision decision layer from main.lua. It takes an
// already-scanned creature list (the transport - memory/vision - is offset-bound
// and lives elsewhere) and returns what the brain needs. No IO, no clock of its
// own, so it is fully unit-testable headless.
public static class ScreenScan
{
    // perigoPerto (n9_socorro.lua:77): wilds that share OUR z and are within
    // Chebyshev distance 3 in x/y. Same z is mandatory - a monster on another floor
    // never threatens us even if the tiles line up.
    public const int DangerDistance = 3;

    // The transport (memory/vision) has not produced a scan this tick. Distinct
    // from a scan that genuinely found nothing.
    public static ScanResult Empty() => new(false, System.Array.Empty<ScannedCreature>(), null);

    public static ScanResult Analyze(IEnumerable<ScannedCreature> creatures)
    {
        var wilds = new List<ScannedCreature>();
        ScannedCreature? mine = null;

        foreach (var c in creatures)
        {
            if (c.Type == ScannedCreature.SummonOwn)
            {
                // First SummonOwn seen is the player's active poke (the rest are
                // duplicates the client may draw for effects).
                mine ??= c;
            }
            else if (c.IsMonster && c.IsAlive)
            {
                wilds.Add(c);
            }
        }

        return new ScanResult(true, wilds, mine);
    }

    // sofaPokeOut() distilled: do we have a LIVE poke on the field? A corpse
    // (hp<=0) drawn a beat before the server removes it does NOT count - counting
    // it held socorro exactly when it was needed (real log 26/08). Returns false
    // when there is no SummonOwn at all.
    public static bool PokeOnField(this ScanResult r) => r.HasPokeOnField;

    // perigoPerto(): how many wilds are glued to us (same z, Chebyshev <= 3).
    // Drives the socorro rhythm and the "is this an emergency" branch everywhere.
    public static int DangerNearby(this ScanResult r, int px, int py, int pz)
    {
        int n = 0;
        foreach (var w in r.Wilds)
            if (w.Z == pz && System.Math.Max(System.Math.Abs(w.X - px), System.Math.Abs(w.Y - py)) <= DangerDistance)
                n++;
        return n;
    }

    // Chebyshev distance (the client's notion of range), matching main.lua's
    // math.max(abs(dx), abs(dy)) used across the cavebot.
    public static int Chebyshev((int X, int Y) a, (int X, int Y) b)
        => System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));
}
