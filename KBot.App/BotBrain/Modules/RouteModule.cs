using KBot.App.Models;

namespace KBot.App.BotBrain;

// Port of n4_rota.lua + cavebot/walking.lua route-walking decision.
// Priority 10: lowest, only acts when nothing higher did.
// Drives a recorded waypoint list purely by position + SEND_KEY, exactly the
// dominant-axis step the existing CavebotNavigator uses. Respawn waiting is a
// per-waypoint flag rather than a new channel.
public sealed class RouteModule : IBotModule
{
    public string Name => "Rota";
    public int Priority => 10;

    private readonly List<(int X, int Y, WaypointAction Action)> _route = new();
    private int _index;
    private const int TileTolerance = 1;

    public bool IsRunning { get; private set; }

    public void Load(IReadOnlyList<(int X, int Y, WaypointAction Action)> route)
    {
        _route.Clear();
        _route.AddRange(route);
        _index = 0;
    }

    public bool Start() { IsRunning = true; _index = 0; return _route.Count > 0; }
    public void Stop() { IsRunning = false; }

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!IsRunning || _index >= _route.Count) return null;
        if (!s.HasPosition) return null; // can't navigate without a fixed position

        var (tx, ty, _) = _route[_index];
        var dx = tx - s.X;
        var dy = ty - s.Y;

        if (Math.Abs(dx) <= TileTolerance && Math.Abs(dy) <= TileTolerance)
        {
            var action = _route[_index].Action;
            _index++;
            // Non-walk waypoint actions are requested via Command for now.
            return action switch
            {
                WaypointAction.Wait => ActionIntent.Command("wait", "300"),
                WaypointAction.Talk => ActionIntent.Command("talk"),
                WaypointAction.Use => ActionIntent.Command("use"),
                WaypointAction.StartAttacker => ActionIntent.Command("attacker:start"),
                WaypointAction.StopAttacker => ActionIntent.Command("attacker:stop"),
                WaypointAction.OrderPokemon => ActionIntent.Command("order"),
                _ => ActionIntent.Move("idle") // Walk: hold, do not press again
            };
        }

        // Dominant-axis single step (matches CavebotNavigator).
        var dir = Math.Abs(dx) >= Math.Abs(dy)
            ? (dx > 0 ? "D" : "A")
            : (dy > 0 ? "S" : "W");
        return ActionIntent.Move(dir);
    }
}
