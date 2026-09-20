using KBot.App.Models;

namespace KBot.App.BotBrain;

// Splits "press something in the client" into the two mechanisms we actually
// have. Keeping them apart means modules/sink stay agnostic of whether a given
// press goes through the named pipe (movement) or straight to the window
// (F-keys, modifiers).
public interface IKeySender
{
    // Movement via the confirmed native SEND_KEY path (WASD/arrows).
    bool TrySendMove(string direction);

    // Any other binding (F-keys, Ctrl+Z, ...) sent to the game window directly.
    bool TrySendHotkey(string combo);
}

// The ActionSink the Brain hands intents to. Routes each intent to the right
// sender channel and reports whether it was accepted. Command intents are
// surfaced through OnCommand so the app can hook in-memory/vision actions later.
public sealed class BrainActionSink : IActionSink
{
    private readonly IKeySender _keys;
    private readonly ICommandResolver _resolver;
    public event Action<ActionIntent>? CommandRequested;

    public string? PendingCommand { get; private set; }

    public BrainActionSink(IKeySender keys, ICommandResolver? resolver = null)
    {
        _keys = keys;
        _resolver = resolver ?? new ProfileCommandResolver();
    }

    public bool Execute(ActionIntent intent, GameState state) => intent.Channel switch
    {
        ActionChannel.Move => _keys.TrySendMove(intent.Payload ?? string.Empty),
        ActionChannel.Hotkey => _keys.TrySendHotkey(intent.Payload ?? string.Empty),
        ActionChannel.Command => RequestCommand(intent),
        _ => false
    };

    private bool RequestCommand(ActionIntent intent)
    {
        var key = _resolver.Resolve(intent, _profileRef);
        if (key is not null && _keys.TrySendHotkey(key))
        {
            PendingCommand = null;
            return true;
        }

        var name = intent.Payload?.Split(':', 2)[0] ?? string.Empty;
        if (name.Length == 0) return false;
        PendingCommand = _resolver.Describe(intent);
        CommandRequested?.Invoke(intent);
        // Report "handled" so the loop advances instead of re-selecting the same
        // module every tick while an offset-bound action stays pending.
        return true;
    }

    // The resolver needs a profile view; the factory sets it after build.
    public void SetProfile(IProfileView profile) => _profileRef = profile;

    private IProfileView _profileRef = new ProfileView(new BotProfile());
}
