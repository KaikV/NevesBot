namespace KBot.App.BotBrain;

// The three ways the bot can act in-game. Every module emits intents; the
// Brain is the only thing that knows how to realise them on the current
// hardware path. Keeping modules channel-agnostic is what lets us swap the
// mechanism (keyboard today, memory-write or vision later) without touching
// the decision logic ported from the Kryon scripts.
public enum ActionChannel
{
    // Fire a named hotkey binding that the player already configured in the
    // client (revive item, food, catch, loot, spells...). The string is exactly
    // what BotProfile stores (e.g. "F9", "Ctrl+Z").
    Hotkey,

    // A movement direction routed through the native SEND_KEY (WASD/arrows).
    Move,

    // A free-form command for future confirmed offsets / in-game API calls.
    // Today it is logged but not dispatched; it exists so ported logic has a
    // home for actions we cannot press yet.
    Command
}

// A single desired effect. Move carries a direction; Hotkey/Command carry text.
public sealed record ActionIntent(ActionChannel Channel, string? Payload = null)
{
    public static ActionIntent Hotkey(string key) => new(ActionChannel.Hotkey, key);
    public static ActionIntent Move(string dir) => new(ActionChannel.Move, dir);
    public static ActionIntent Command(string name, string? arg = null) => new(ActionChannel.Command, string.IsNullOrWhiteSpace(arg) ? name : $"{name}:{arg}");

    public override string ToString() => Channel switch
    {
        ActionChannel.Hotkey => $"hotkey {Payload}",
        ActionChannel.Move => $"move {Payload}",
        _ => $"cmd {Payload}"
    };
}

// Executed by the Brain after a module decides. Implementations decide whether
// an intent is actually supported right now; returning false lets the module
// fall back to a degraded plan instead of spamming an action that no-ops.
public interface IActionSink
{
    bool Execute(ActionIntent intent, GameState state);
}
