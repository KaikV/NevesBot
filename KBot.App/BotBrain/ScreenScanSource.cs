using KBot.App.Models;

namespace KBot.App.BotBrain;

// Transport seam for ETAPA 2. Turns "what the lifecycle knows this tick" into one
// screen pass (live wilds / my poke / other players). Today the only confirmed read
// is position; creature offsets still do not exist (ROADMAP ETAPA 2), so the
// default source returns null and the provider keeps those fields UNKNOWN instead
// of guessing. The brain never touches memory or vision directly - it only consumes
// whatever the injected source hands it, which is what keeps every module headless.
public interface IScreenScanSource
{
    // Null = the transport produced nothing this tick (offline / no offsets yet).
    // Callers must treat null as UNKNOWN, never as an empty screen.
    ScanResult? GetScan(NativeStatus? status, CharacterPresence presence);
}

// Used until a real memory/vision transport exists. Always "no scan", so the
// provider leaves EnemyCount / Wilds / FieldHasPoke unknown - behaviourally
// identical to the old hardcoded path, but swappable in one line. Wiring a real
// source is what lights up ETAPA 2 without touching the brain or the modules.
public sealed class NoScreenScanSource : IScreenScanSource
{
    public ScanResult? GetScan(NativeStatus? status, CharacterPresence presence) => null;
}

// Adapter for the day the transport exists. Feed a raw creature list (exactly what
// the C++ reader or the vision detector will eventually emit) and every tick re-runs
// the pure decision layer over it. The Func lets the source re-read live memory on
// each call instead of caching a stale snapshot.
public static class ScreenScanSource
{
    public static IScreenScanSource FromCreatures(Func<IReadOnlyList<ScannedCreature>> creatures)
        => new CreatureScanSource(creatures);

    private sealed class CreatureScanSource : IScreenScanSource
    {
        private readonly Func<IReadOnlyList<ScannedCreature>> _creatures;
        public CreatureScanSource(Func<IReadOnlyList<ScannedCreature>> creatures) => _creatures = creatures;
        public ScanResult? GetScan(NativeStatus? status, CharacterPresence presence)
            => ScreenScan.Analyze(_creatures());
    }
}
