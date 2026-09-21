namespace KBot.App.BotBrain;

// One pokeball slot of the local player's pokebar, as the game reports it.
// Health is 0..100 (0 = fainted). This mirrors modules.game_pokebar's
// getPlayerPokeballs() entry {name, health, uuid}. The pokebar is a LOCAL COPY
// that can lie (go stale) - which is exactly why socorro cross-checks the field.
public sealed record PokebarSlot(string Name, double HealthPercent, string Uuid = "")
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Name != "0";
    public bool IsFainted => HealthPercent <= 0;
}

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

    // Party / pokebar (ETAPA 1). Empty list = we cannot read it yet, so modules
    // that need the party degrade gracefully instead of guessing a slot.
    // Mirrors modules.game_pokebar.getPlayerPokeballs(). The pokebar is a local
    // copy that can lie (go stale), which is why socorro cross-checks the field.
    public IReadOnlyList<PokebarSlot> Pokebar { get; init; } = System.Array.Empty<PokebarSlot>();
    // Which slot the GAME believes is on the field (getActiveSlot, 1-based),
    // null if we can't read it yet.
    public int? ActivePokebarSlot { get; init; }

    // Field truth (ETAPA 2 / sofaPokeOut): is one of OUR pokemon visible on the
    // screen right now? Null = unknown (vision pending). This is the honest
    // signal socorro trusts over the pokebar.
    public bool? FieldHasPoke { get; init; }
    // How many wild creatures are within reach of us (perigoPerto). Drives the
    // socorro tempo: with a wild glued to us the rhythm shrinks to a sprint.
    public int WildsNearby { get; init; }

    // "Pulled" = the player got dragged by a move/trap. Set by vision/alerts.
    public bool Pulled { get; init; }

    // Millisecond wall clock captured when the snapshot was built.
    public long NowMs { get; init; }

    public static GameState Empty(long nowMs) => new()
    {
        ClientConnected = false, InGame = false, HasPosition = false, NowMs = nowMs
    };
}
