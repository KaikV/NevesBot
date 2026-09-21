namespace KBot.App.BotBrain;

// Pure port of main.lua's sofaRareWild / auto-target decision: given the wilds on
// screen and the player's position, choose WHICH one to aim at. No IO, no clock -
// everything it needs is an argument, so it is fully unit-testable headless. The
// transport (the screen scan that produces the list) lives elsewhere.
public static class TargetSelection
{
    // A rare/shiny word matches when it is a (case-insensitive) SUBSTRING of the
    // creature's name - main.lua searches the whole display name so any server prefix
    // ("Shiny Mawile [169]") works. Empty/whitespace words are skipped.
    private static bool HasRareWord(string name, IReadOnlyList<string> words)
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

    // Pick the target, or null when nothing valid is on screen.
    //   1. If RareFirst and any in-range wild matches a rare/shiny word -> the CLOSEST
    //      such one wins (total priority over distance to normal targets).
    //   2. Otherwise -> the closest in-range, same-floor alive wild.
    // "In range" = Chebyshev <= range AND same floor, exactly the client's notion of
    // reach used across the cavebot. Ties on distance keep the first one seen.
    public static ScannedCreature? Pick(
        IEnumerable<ScannedCreature> wilds,
        int px, int py, int pz,
        int range,
        bool rareFirst,
        IReadOnlyList<string> rareWords)
    {
        ScannedCreature? bestRare = null;
        int bestRareD = int.MaxValue;
        ScannedCreature? best = null;
        int bestD = int.MaxValue;

        foreach (var w in wilds)
        {
            if (!w.IsMonster || !w.IsAlive) continue;          // only alive monsters
            if (w.Z != pz) continue;                            // other floor: out of reach
            var d = System.Math.Max(System.Math.Abs(w.X - px), System.Math.Abs(w.Y - py));
            if (d > range) continue;                            // beyond attack range

            if (rareFirst && HasRareWord(w.Name, rareWords))
            {
                if (d < bestRareD) { bestRareD = d; bestRare = w; }
            }
            if (d < bestD) { bestD = d; best = w; }             // plain closest (any)
        }

        return bestRare ?? best;
    }
}
