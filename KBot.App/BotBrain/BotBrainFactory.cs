using KBot.App.Models;
using KBot.App.Services;

namespace KBot.App.BotBrain;

// Wires the app's services into a ready-to-run BotBrain. The caller supplies a
// stateProvider that reads whatever the lifecycle currently knows (native status
// + presence); this keeps the brain decoupled from KBotLifecycle's internals.
public static class BotBrainFactory
{
    // The canonical module set, shared by the legacy brain AND the hybrid KBotEngine so the two
    // never drift. Ordering here is irrelevant (both owners sort by Priority themselves).
    public static IBotModule[] Modules() => new IBotModule[]
    {
        new VigiaModule(),
        new HealingModule(),
        new SocorroModule(),
        new EndgameModule(),
        new TargetingModule(),
        new CatchModule(),
        new LootModule(),
        new AlertsModule(),
        new FishingModule(),
        new RouteModule(),
        new AntiAfkModule(),
    };

    public static BotBrain Build(NativeService native, nint hwnd, BotProfile profile, Func<GameState> stateProvider)
    {
        var keys = new WindowsKeySender(native, hwnd);
        var sink = new BrainActionSink(keys);
        var view = new ProfileView(profile);
        sink.SetProfile(view);

        var brain = new BotBrain(sink, stateProvider, view);
        brain.Register(Modules());
        return brain;
    }
}
