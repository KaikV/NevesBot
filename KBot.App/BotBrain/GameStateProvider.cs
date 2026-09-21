using KBot.App.Models;

namespace KBot.App.BotBrain;

// Turns the confirmed native reads (+ character presence) into a GameState for
// one tick. Anything not yet readable (HP, active, battle) stays null so modules
// skip rather than guess. Position is the only fully-confirmed signal today.
public static class GameStateProvider
{
    public static GameState From(NativeStatus? status, CharacterPresence presence, long nowMs)
        => From(status, presence, nowMs, null);

    // Overload that accepts one screen scan (ETAPA 2 / _cbScreen). The scan is the
    // honest source: EnemyCount and FieldHasPoke come straight from it. A null scan
    // means "vision produced nothing this tick", so those fields stay unknown.
    public static GameState From(NativeStatus? status, CharacterPresence presence, long nowMs, ScanResult? scan)
    {
        if (status is null || !status.NativeOnline || !status.ClientFound)
            return GameState.Empty(nowMs);

        bool hasScan = scan is { HasRead: true };

        return new GameState
        {
            ClientConnected = status.ClientFound,
            InGame = presence == CharacterPresence.InGame,
            HasPosition = status.HasPosition,
            X = status.PosX,
            Y = status.PosY,
            Z = status.PosZ,
            InBattle = null,
            // A live read tells us how many wilds are on screen and whether OUR
            // poke is standing out there - the two signals socorro/targeting need.
            EnemyCount = hasScan ? scan!.Wilds.Count : null,
            ActiveHpPercent = null,
            ActiveAlive = null,
            FieldHasPoke = hasScan ? scan!.PokeOnField() : null,
            WildsNearby = hasScan && status.HasPosition
                ? scan!.DangerNearby(status.PosX, status.PosY, status.PosZ)
                : 0,
            Pulled = false,
            NowMs = nowMs
        };
    }
}
