using KBot.App.BotBrain;
using KBot.App.Engine.State;
using KBot.App.Models;

namespace KBot.App.Engine.Actions
{
    /// <summary>
    /// What actually happened when we tried to fire an intent. Kept separate from the boolean sink
    /// result because the legacy BrainActionSink collapses "pressed" and "offset-bound pending" into a
    /// single true - which is exactly the lie the engine exists to remove. The executor distinguishes:
    /// Pressed = we sent a key/move; Pending = we know WHAT to do but can't realise it yet (no offset /
    /// no binding); Rejected = the transport refused us (window lost, pipe down).
    /// </summary>
    public enum FireOutcome
    {
        Pressed,   // a real input reached the client
        Pending,   // resolvable-to-something later; not a failure, just "not yet realisable"
        Rejected,  // we asked the transport and it said no
    }

    public sealed record FireResult(bool Accepted, FireOutcome Outcome, string Detail);

    /// <summary>
    /// The executor contract: given the winning IntentV2 from the ActionManager, DO the thing in the
    /// game client and report the HONEST outcome. Accepted==true only when the hardware path accepted the
    /// input (key sent / move queued); Pending and Rejected both give Accepted==false so the confirmation
    /// step (FASE I) decides whether to retry or abort - we never pretend success.
    /// </summary>
    public interface IActionExecutor
    {
        FireResult Execute(IntentV2 intent, GameStateSnapshot snapshot);
    }

    /// <summary>
    /// Bridges the V2 intent pipeline into the EXISTING hardware paths (IKeySender + ICommandResolver).
    /// It converts IntentV2 → legacy ActionIntent via ToLegacy() and routes exactly the way the legacy
    /// BrainActionSink does - except it tells the truth about Commands: an unresolvable command is
    /// Pending (not "handled"), so the arbiter's retry/abort ladder gets a real signal.
    ///
    /// During the dual-run window this produces byte-identical physical output to the old sink. When a
    /// native/vision-specific executor lands later, we swap the IActionExecutor binding without touching
    /// modules or the ActionManager.
    /// </summary>
    public sealed class ActionExecutor : IActionExecutor
    {
        private readonly IKeySender _keys;
        private readonly ICommandResolver _resolver;
        private IProfileView _profile = new ProfileView(new BotProfile());

        public ActionExecutor(IKeySender keys, ICommandResolver? resolver = null)
        {
            _keys = keys;
            _resolver = resolver ?? new ProfileCommandResolver();
        }

        public void SetProfile(IProfileView profile) => _profile = profile;

        public FireResult Execute(IntentV2 intent, GameStateSnapshot snapshot)
        {
            var legacy = intent.ToLegacy();
            return legacy.Channel switch
            {
                ActionChannel.Move    => SendMove(legacy),
                ActionChannel.Hotkey  => SendHotkey(legacy),
                ActionChannel.Command => ResolveCommand(legacy),
                _ => new FireResult(false, FireOutcome.Rejected, $"canal sem executor: {legacy.Channel}"),
            };
        }

        private FireResult SendMove(ActionIntent legacy)
        {
            var dir = legacy.Payload ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dir))
                return new FireResult(false, FireOutcome.Rejected, "move sem direcao");
            return _keys.TrySendMove(dir) ? new FireResult(true, FireOutcome.Pressed, $"move {dir}")
                                          : new FireResult(false, FireOutcome.Rejected, $"pipe recusou move {dir}");
        }

        private FireResult SendHotkey(ActionIntent legacy)
        {
            var combo = legacy.Payload ?? string.Empty;
            if (string.IsNullOrWhiteSpace(combo))
                return new FireResult(false, FireOutcome.Rejected, "hotkey vazia");
            return _keys.TrySendHotkey(combo) ? new FireResult(true, FireOutcome.Pressed, $"hotkey {combo}")
                                              : new FireResult(false, FireOutcome.Rejected, $"janela recusou {combo}");
        }

        private FireResult ResolveCommand(ActionIntent legacy)
        {
            var resolved = _resolver.Resolve(legacy, _profile);
            if (resolved is not null && _keys.TrySendHotkey(resolved))
                return new FireResult(true, FireOutcome.Pressed, $"cmd -> {resolved}");

            // We KNOW the action but can't press it yet (no offset / no binding). Honest pending, not handled.
            return new FireResult(false, FireOutcome.Pending, _resolver.Describe(legacy));
        }
    }
}
