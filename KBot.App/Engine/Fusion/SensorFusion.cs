using KBot.App.BotBrain;
using KBot.App.Engine.Scheduler;
using KBot.App.Engine.Sensors;
using KBot.App.Engine.State;

namespace KBot.App.Engine.Fusion
{
    /// <summary>
    /// Turns the latest sensor samples (from <see cref="SensorSampleStore"/>) into one fused
    /// <see cref="GameStateSnapshot"/> per tick. It is the ONLY place that:
    ///   1. applies a <see cref="FreshnessPolicy"/> per data type (HP vs position vs creatures...),
    ///   2. resolves primary/fallback sources when a type has more than one feed,
    ///   3. records per-source health so the UI/logs can say "vision dropped, using memory".
    ///
    /// Fusion is READ-ONLY on the store: it never triggers a capture and never blocks, so a slow
    /// sensor can only make its data look stale, never stall the brain tick.
    /// </summary>
    public sealed class SensorFusion
    {
        private readonly long _nowMs;
        private readonly FreshnessPolicy _positionPolicy;
        private readonly FreshnessPolicy _creaturesPolicy;
        private readonly FreshnessPolicy _presencePolicy;

        public SensorFusion(long? nowMs = null,
            FreshnessPolicy? position = null,
            FreshnessPolicy? creatures = null,
            FreshnessPolicy? presence = null)
        {
            _nowMs = nowMs ?? Environment.TickCount64;
            _positionPolicy = position ?? DefaultPolicies.Position;
            _creaturesPolicy = creatures ?? DefaultPolicies.Creatures;
            _presencePolicy = presence ?? DefaultPolicies.Presence;
        }

        private TimeSpan Age(long capturedAtMs) => TimeSpan.FromMilliseconds(_nowMs - capturedAtMs);

        public GameStateSnapshot Fuse(SensorSampleStore store)
        {
            // --- presence / connectivity ---
            var presenceFresh = DataFreshness.Unknown;
            var inGame = false;
            if (store.TryGet<SensorValue<PresenceValue>>(VisionPresenceSensor.NameConst, out var presence) && presence is { IsValid: true })
            {
                presenceFresh = _presencePolicy.Evaluate(Age(presence.CapturedAt));
                inGame = presence.Value!.InGame;
            }

            // --- position (structured pipe today; a visual position feed would resolve here later) ---
            var posFresh = DataFreshness.Unknown;
            int? px = null, py = null, pz = null;
            var clientOnline = false;
            if (store.TryGet<SensorValue<PositionValue>>(StructuredPositionSensor.NameConst, out var pos) && pos is not null)
            {
                // A "client connected but no position" sample still tells us the pipe is up.
                clientOnline = pos.Health != SensorHealth.Unavailable;
                if (pos.IsValid && pos.Value is { } v)
                {
                    posFresh = _positionPolicy.Evaluate(Age(pos.CapturedAt));
                    px = v.X; py = v.Y; pz = v.Z;
                    clientOnline = v.ClientOnline;
                }
            }

            // --- creatures (visual today; structured offsets would be a second feed resolved by priority) ---
            var creaturesFresh = DataFreshness.Unknown;
            var creaturesRead = false;
            IReadOnlyList<ScannedCreature> wilds = Array.Empty<ScannedCreature>();
            ScannedCreature? myPoke = null;
            IReadOnlyList<ScannedCreature> others = Array.Empty<ScannedCreature>();
            int wildsNearby = 0;
            bool? fieldHasPoke = null;
            if (store.TryGet<SensorValue<CreaturesValue>>(VisualCreatureSensor.NameConst, out var cr) && cr is { IsValid: true })
            {
                var scan = cr.Value!.Scan;
                if (scan.HasRead)
                {
                    creaturesFresh = _creaturesPolicy.Evaluate(Age(cr.CapturedAt));
                    creaturesRead = true;
                    // The visual feed reports creatures RELATIVE to the player (0,0). The rest of the
                    // codebase (GameState, TargetSelection, RouteModule, ...) works in ABSOLUTE client
                    // coords. Fusion is the one place that sees both the player position and the scan,
                    // so it normalizes here to one shared world frame. Without a position we have no
                    // anchor, so creatures stay relative (0 = player) rather than being guessed.
                    var hasAnchor = px is not null && py is not null && pz is not null;
                    wilds = Absolute(scan.Wilds, px, py, pz);
                    myPoke = ScanMyPoke(scan.MyPoke, px, py, pz);
                    others = Absolute(scan.Others, px, py, pz);
                    fieldHasPoke = scan.PokeOnField();
                    if (hasAnchor)
                        // PerigoPerto for a vision feed: 2D Chebyshev from the player tile (the
                        // scan's origin). Vision carries no depth, so - unlike the memory rule -
                        // there is no same-floor gate to apply; distance is what we can see.
                        wildsNearby = Nearby2D(scan.Wilds);
                }
            }

            return new GameStateSnapshot(
                SnapshotId: Guid.NewGuid(),
                CapturedAt: _nowMs,
                ClientOnline: clientOnline,
                InGame: inGame,
                PositionFreshness: posFresh,
                PosX: px, PosY: py, PosZ: pz,
                CreaturesFreshness: creaturesFresh,
                CreaturesRead: creaturesRead,
                Wilds: wilds,
                MyPoke: myPoke,
                Others: others,
                WildsNearby: wildsNearby,
                FieldHasPoke: fieldHasPoke,
                PlayerHpPercent: null,   // pending real offsets/vision - stays Unknown until readable
                ActiveHpPercent: null,
                ActiveAlive: null);
        }

        // The scan is player-relative; adding the anchor yields shared-world coords. Vision has no
        // depth channel, so a detected creature is placed on the PLAYER'S floor (its z) - the one
        // honest assumption: what we see around us is on our current level.
        private static IReadOnlyList<ScannedCreature> Absolute(
            IReadOnlyList<ScannedCreature> list, int? px, int? py, int? pz)
        {
            if (px is null || py is null || pz is null) return list;
            return list.Select(c => new ScannedCreature(c.Type, c.HealthPercent, c.X + px.Value, c.Y + py.Value, pz.Value, c.Name)).ToArray();
        }

        private static ScannedCreature? ScanMyPoke(
            ScannedCreature? my, int? px, int? py, int? pz)
        {
            if (my is null || px is null || py is null || pz is null) return my;
            return new ScannedCreature(my.Type, my.HealthPercent, my.X + px.Value, my.Y + py.Value, pz.Value, my.Name);
        }

        // 2D perigoPerto in the scan's player-relative frame (the player is the origin).
        private static int Nearby2D(IReadOnlyList<ScannedCreature> wilds)
        {
            int n = 0;
            foreach (var w in wilds)
                if (Math.Max(Math.Abs(w.X), Math.Abs(w.Y)) <= ScreenScan.DangerDistance)
                    n++;
            return n;
        }
    }
}
