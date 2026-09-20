using KBot.App.Models;

namespace KBot.App.BotBrain;

// Turns the confirmed native reads (+ character presence) into a GameState for
// one tick. Anything not yet readable (HP, active, battle) stays null so modules
// skip rather than guess. Position is the only fully-confirmed signal today.
public static class GameStateProvider
{
    public static GameState From(NativeStatus? status, CharacterPresence presence, long nowMs)
    {
        if (status is null || !status.NativeOnline || !status.ClientFound)
            return GameState.Empty(nowMs);

        return new GameState
        {
            ClientConnected = status.ClientFound,
            InGame = presence == CharacterPresence.InGame,
            HasPosition = status.HasPosition,
            X = status.PosX,
            Y = status.PosY,
            Z = status.PosZ,
            // Below is intentionally null/unknown until offsets/vision confirm it:
            InBattle = null,
            EnemyCount = null,
            ActiveHpPercent = null,
            ActiveAlive = null,
            Pulled = false,
            NowMs = nowMs
        };
    }
}
