namespace KBot.App.Engine.Events
{
    /// <summary>
    /// A change between two consecutive <see cref="State.GameStateSnapshot"/> ticks. Modules and the
    /// UI react to EVENTS (edges), not raw state — "my poke fainted" fires once, not every tick while
    /// it is fainted. A delta carries what changed and both sides, so handlers can log/reason about it.
    /// </summary>
    public enum GameDeltaType
    {
        // connectivity / session
        EnteredGame,
        LeftGame,
        ClientLost,
        ClientRestored,

        // player position
        PositionLost,
        PositionRestored,

        // our pokemon on the field (the socorro-critical signal)
        PokeEnteredField,
        PokeLeftField,
        PokeFainted,
        PokeRecovered,

        // threat level
        WildsAppeared,
        WildsCleared,
    }

    public sealed record GameDelta(
        GameDeltaType Type,
        long AtMs,
        string Source = "DeltaEngine")
    {
        public string Code => Type.ToString().ToLowerInvariant();
        public string Describe() => Type switch
        {
            GameDeltaType.EnteredGame => "personagem entrou no jogo",
            GameDeltaType.LeftGame => "personagem saiu do jogo",
            GameDeltaType.ClientLost => "cliente/detach perdido",
            GameDeltaType.ClientRestored => "cliente reconectado",
            GameDeltaType.PositionLost => "posicao deixou de ser legivel",
            GameDeltaType.PositionRestored => "posicao voltou a ser legivel",
            GameDeltaType.PokeEnteredField => "nosso poke entrou no campo",
            GameDeltaType.PokeLeftField => "nosso poke saiu do campo",
            GameDeltaType.PokeFainted => "nosso poke desmaiou",
            GameDeltaType.PokeRecovered => "nosso poke recuperou no campo",
            GameDeltaType.WildsAppeared => "inimigos apareceram na tela",
            GameDeltaType.WildsCleared => "nenhum inimigo na tela",
            _ => Type.ToString()
        };
    }
}
