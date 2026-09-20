namespace KBot.App.BotBrain;

// Snapshot of everything the bot knows about the game at one instant.
// Fields that depend on still-unconfirmed memory offsets are nullable: a null
// value means "we can't read this yet", and modules must degrade gracefully
// instead of assuming zero. Position comes from the confirmed DX/GL offsets.
public sealed record GameState
{
    public required bool ClientConnected { get; init; }
    public required bool InGame { get; init; }

    public bool HasPosition { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }

    // Battle / combat awareness. Null = unknown (offset pending or vision off).
    public bool? InBattle { get; init; }
    public int? EnemyCount { get; init; }      // number of hostile creatures on screen
    public double? ActiveHpPercent { get; init; } // our active pokemon hp (0..100), null if unknown
    public bool? ActiveAlive { get; init; }     // null if unknown; false => fainted

    // "Pulled" = the player got dragged by a move/trap. Set by vision/alerts.
    public bool Pulled { get; init; }

    // Millisecond wall clock captured when the snapshot was built.
    public long NowMs { get; init; }

    public static GameState Empty(long nowMs) => new()
    {
        ClientConnected = false, InGame = false, HasPosition = false, NowMs = nowMs
    };
}
