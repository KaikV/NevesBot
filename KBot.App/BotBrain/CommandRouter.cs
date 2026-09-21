namespace KBot.App.BotBrain;

// Turns a free-form Command intent into something we can DO with today's
// hardware path. Some ported actions already have a player-configured key
// (catch, loot, fishing) -> resolve to that hotkey. Actions that need a not-yet
// confirmed offset (casting spells, sending out a pokemon, talking to an NPC)
// resolve to null and are reported as pending instead of being pressed blind.
public interface ICommandResolver
{
    // Hotkey/combo to press for this command, or null if not realisable yet.
    string? Resolve(ActionIntent command, IProfileView profile);

    // Human phrase (pt-BR) describing the state, for the dashboard/log.
    string Describe(ActionIntent command);
}

// Profile-driven resolver: presses a configured key when the feature is both
// enabled and has a binding, and names the gap otherwise.
public sealed class ProfileCommandResolver : ICommandResolver
{
    public string? Resolve(ActionIntent command, IProfileView p)
    {
        var name = CommandName(command);
        return name switch
        {
            "catch" => p.CatchEnabled && !string.IsNullOrWhiteSpace(p.CatchHotkey) ? p.CatchHotkey : null,
            "loot" => p.LootEnabled && !string.IsNullOrWhiteSpace(p.LootHotkey) ? p.LootHotkey : null,
            "fish" => p.FishingEnabled && !string.IsNullOrWhiteSpace(p.FishingHotkey) ? p.FishingHotkey : null,
            _ => null
        };
    }

    public string Describe(ActionIntent command) => CommandName(command) switch
    {
        "attack" => "atacar (aguarda offset de skill/batalha)",
        "aim" => $"mirar em {command.Payload?.Split(':', 2)[1] ?? "?"} (aguarda offset de criatura)",
        "summon" => "soltar poke (aguarda slot de pokemon)",
        "talk" => "falar com NPC (aguarda offset de diálogo)",
        "use" => "usar item em alvo (aguarda offset)",
        "order" => "reordenar pokémon (aguarda offset)",
        "attacker" => "iniciar/parar atacante (aguarda offset)",
        "wait" => "aguardar waypoint",
        "alert" => "enviar alerta (camada de app)",
        "catch" => "capturar",
        "loot" => "coletar",
        "fish" => "pesca",
        "eghold" => "END GAME dono do poke (fase do combo em andamento)",
        "pokestop" => "falar !pokestop no chat (aguarda canal de chat)",
        _ => $"comando {command.Payload}"
    };

    private static string CommandName(ActionIntent command)
        => command.Payload?.Split(':', 2)[0] ?? string.Empty;
}
