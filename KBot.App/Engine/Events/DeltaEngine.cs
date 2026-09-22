using KBot.App.Engine.State;

namespace KBot.App.Engine.Events
{
    /// <summary>
    /// Stateful edge detector: given a fresh <see cref="GameStateSnapshot"/> each tick, it emits the
    /// <see cref="GameDelta"/>s that separate it from the previous one. Modules react to these events,
    /// never to raw state, so "poke fainted" fires exactly once even while it stays fainted.
    ///
    /// Honesty: a transition is only emitted when BOTH sides of the edge are trustworthy. A field we
    /// could not read on either side produces NO delta — "unknown → unknown" is not a change. This is
    /// what stops a flickering sensor from firing Enter/Leave-Field spam.
    ///
    /// Pure and clock-free except for the AtMs we stamp with the snapshot's own CapturedAt, so it is
    /// fully deterministic and unit-testable headless.
    /// </summary>
    public sealed class DeltaEngine
    {
        private GameStateSnapshot? _prev;
        private bool _prevWildsKnown;
        private int _prevWilds;

        public void Reset()
        {
            _prev = null;
            _prevWildsKnown = false;
            _prevWilds = 0;
        }

        public List<GameDelta> Detect(GameStateSnapshot cur)
        {
            var prev = _prev;
            _prev = cur;
            int nowWilds = cur.CreaturesRead ? cur.Wilds.Count : -1;   // -1 == unreadable

            if (prev is null)
            {
                // First tick: seed the wilds baseline (no event) so the NEXT tick can see a change.
                SeedWilds(nowWilds);
                return New(cur.CapturedAt);
            }

            var d = New(cur.CapturedAt);
            var t = cur.CapturedAt;

            // --- connectivity ---
            if (prev.ClientOnline && !cur.ClientOnline) d.Add(Make(GameDeltaType.ClientLost, t));
            if (!prev.ClientOnline && cur.ClientOnline) d.Add(Make(GameDeltaType.ClientRestored, t));

            // --- session presence (both sides must be readable) ---
            if (cur.InGame != prev.InGame)
                d.Add(Make(cur.InGame ? GameDeltaType.EnteredGame : GameDeltaType.LeftGame, t));

            // --- position readability ---
            if (prev.HasPosition && !cur.HasPosition) d.Add(Make(GameDeltaType.PositionLost, t));
            if (!prev.HasPosition && cur.HasPosition) d.Add(Make(GameDeltaType.PositionRestored, t));

            // --- our pokemon on the field: the socorro-critical edge. ---
            // Only emitted between TWO readable creature frames. A missing scan keeps us silent:
            // "vision blinked" is not the poke leaving the field, and a re-sync is not it entering.
            if (cur.CreaturesRead && prev.CreaturesRead)
            {
                bool nowOn = cur.FieldHasPoke == true;
                bool prevOn = prev.FieldHasPoke == true;
                if (nowOn && !prevOn) d.Add(Make(GameDeltaType.PokeEnteredField, t));
                else if (!nowOn && prevOn) d.Add(Make(GameDeltaType.PokeLeftField, t));

                // ActiveHp/ActiveAlive arrive with real offsets; the fainted edge only fires once
                // both sides are known (null -> false is "we can read it now", not a faint).
                bool nowFainted = cur.ActiveAlive == false;
                bool wasFainted = prev.ActiveAlive == false;
                if (nowFainted && !wasFainted && prev.ActiveAlive is not null) d.Add(Make(GameDeltaType.PokeFainted, t));
                else if (!nowFainted && wasFainted) d.Add(Make(GameDeltaType.PokeRecovered, t));
            }

            // --- threat level: wild count crossing zero, only between two real reads ---
            if (nowWilds >= 0 && _prevWildsKnown)
            {
                if (_prevWilds <= 0 && nowWilds > 0) d.Add(Make(GameDeltaType.WildsAppeared, t));
                else if (_prevWilds > 0 && nowWilds <= 0) d.Add(Make(GameDeltaType.WildsCleared, t));
            }
            SeedWilds(nowWilds);

            return d;
        }

        // Refreshes the stored baseline so the next tick has something to compare against. When a
        // read is unreadable we KEEP the last known value (do not clobber with garbage).
        private void SeedWilds(int nowWilds)
        {
            if (nowWilds < 0) return;          // unreadable: leave the old baseline intact
            _prevWildsKnown = true;
            _prevWilds = nowWilds;
        }

        private static List<GameDelta> New(long atMs) => new();
        private static GameDelta Make(GameDeltaType type, long atMs) => new(type, atMs);
    }
}
