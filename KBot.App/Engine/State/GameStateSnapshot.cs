using KBot.App.BotBrain;
using KBot.App.Engine.Fusion;
using KBot.App.Engine.Sensors;

namespace KBot.App.Engine.State
{
    /// <summary>
    /// The fused, immutable game state for one tick. Built by <see cref="Fusion.SensorFusion"/>
    /// from the latest sensor samples, NOT by any single transport. This is the single source of
    /// truth the modules read; nothing downstream knows where a value came from.
    ///
    /// Honesty contract (the whole point of the rewrite):
    ///  - Every data field carries its FRESHNESS, so a module sees "position is stale" instead of
    ///    a silently-old coordinate it mistakes for truth.
    ///  - Creature data carries <see cref="CreaturesRead"/>: false = the scan never ran this tick
    ///    (no vision / window lost), which must NEVER be read as "the screen is empty".
    ///  - Unknown stays unknown: unreadable fields are null, never a guessed 0/false.
    ///
    /// It is deliberately dependency-light (only core model types + the freshness/health enums),
    /// so it is trivially constructible in headless tests and serializable later for logs/replay.
    /// </summary>
    public sealed record GameStateSnapshot(
        Guid SnapshotId,
        long CapturedAt,
        bool ClientOnline,
        bool InGame,
        // --- player ---
        DataFreshness PositionFreshness,
        int? PosX,
        int? PosY,
        int? PosZ,
        // --- combat / creatures ---
        DataFreshness CreaturesFreshness,
        bool CreaturesRead,
        IReadOnlyList<ScannedCreature> Wilds,
        ScannedCreature? MyPoke,
        IReadOnlyList<ScannedCreature> Others,
        int WildsNearby,
        bool? FieldHasPoke,
        // HP still pending real offsets/vision -> honest null until readable.
        double? PlayerHpPercent,
        double? ActiveHpPercent,
        bool? ActiveAlive)
    {
        public bool HasPosition => PosX is not null && PosY is not null && PosZ is not null;

        /// <summary>
        /// Dual-read bridge to the legacy <see cref="GameState"/> so existing modules keep working
        /// UNCHANGED while the engine migrates. Unknown fields map to null; a missing scan maps to
        /// HasScreenScan=false (never a fabricated empty list). This is a lossy projection ON PURPOSE.
        /// </summary>
        public GameState ToLegacy()
        {
            var hasPos = HasPosition;
            return new GameState
            {
                ClientConnected = ClientOnline,
                InGame = InGame,
                HasPosition = hasPos,
                X = PosX ?? 0,
                Y = PosY ?? 0,
                Z = PosZ ?? 0,
                InBattle = null,
                EnemyCount = CreaturesRead ? Wilds.Count : null,
                Wilds = CreaturesRead ? Wilds : Array.Empty<ScannedCreature>(),
                HasScreenScan = CreaturesRead,
                FieldHasPoke = CreaturesRead ? FieldHasPoke : null,
                WildsNearby = CreaturesRead ? WildsNearby : 0,
                OtherPlayerPresent = CreaturesRead ? (Others.Count > 0) : null,
                PlayerHpPercent = PlayerHpPercent,
                ActiveHpPercent = ActiveHpPercent,
                ActiveAlive = ActiveAlive,
                NowMs = CapturedAt
            };
        }
    }
}
