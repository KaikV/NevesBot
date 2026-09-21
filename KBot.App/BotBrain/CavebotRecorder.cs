namespace KBot.App.BotBrain;

// One step the recorder observed: where the player WAS and where they ARE now.
// The native poll delivers these ~70ms apart, NOT one game step - so a fast player
// moves 2+ tiles between them. That is why "floor changed" alone does not mean
// "stairs" (a mount on flat ground would), see OnStep below.
public readonly record struct StepEvent(int X, int Y, int Z);

// What the recorder should stamp for this step (recorder.lua onPlayerPositionChange).
public enum RecordAction
{
    None,    // no waypoint: too close to the last one / bot-driven / just used an item
    Position,// normal goto at the same floor
    Stairs   // floor change -> playback must STEP ONTO the tile before using it
}

// Pure port of cavebot/recorder.lua's stamping decision. No IO, no clock - it takes
// everything it needs as arguments (the random seed and "now") so it is unit-testable
// headless. The transport (the native poll that feeds StepEvent) lives elsewhere.
public static class CavebotRecorder
{
    // How far (Chebyshev, same floor) the player must travel from the LAST stamped
    // waypoint before another is stamped. recorder.lua: storage.recordDist is used as-is
    // when it is a sane fixed value (1-9); otherwise the distance is ROLLED ONCE per
    // session (1-5) so two characters from the same cave do not end up with identical,
    // suspiciously regular routes. The roll must survive script reloads (that is why it
    // lived in _G) - so we pass it in already-rolled, not here.
    public static int Dist(int configured, int sessionRoll)
    {
        if (configured >= 1 && configured <= 9) return configured;
        return System.Math.Clamp(sessionRoll, 1, 5);
    }

    // The window in which a floor change is blamed on the item we JUST used instead of
    // a fresh staircase (recorder.lua: justUsedAt + 2500ms). Stamping a redundant
    // stairs-goto made playback both walk onto the tile AND use it.
    public const long RecentUseMs = 2500;

    // Decide what, if anything, to stamp for one observed step.
    //   last      : the previous waypoint (null on the very first step)
    //   roll      : this session's rolled distance (1-5); ignored when configured is valid
    //   nowMs     : wall clock, only used against lastUseAtMs
    public static RecordAction OnStep(
        StepEvent step,
        StepEvent? last,
        int configuredDist,
        int roll,
        long nowMs,
        long? lastUseAtMs)
    {
        var dist = Dist(configuredDist, roll);

        if (last is not StepEvent prev)
            return RecordAction.Position; // first step: drop the player where we found them

        // Floor change right after using an item on the ground: the USE action already
        // captured that transition, so do not also stamp a staircase.
        if (prev.Z != step.Z && lastUseAtMs is long usedAt && nowMs - usedAt < RecentUseMs)
            return RecordAction.None;

        // Only a Z change means we took a real staircase/hole (up and down keep x,y, so
        // we stamp the tile we are now above/below but still tagged with the OLD floor -
        // findStairTile's geometric fallback).
        if (prev.Z != step.Z)
            return RecordAction.Stairs;

        // Same floor: stamp only once we are far enough from the last waypoint. This is
        // where a fast player (2+ tiles per poll) and same-floor teleports both land, as
        // a clean goto - never as a fake staircase.
        bool farEnough = System.Math.Max(System.Math.Abs(step.X - prev.X),
                                         System.Math.Abs(step.Y - prev.Y)) >= dist;
        return farEnough ? RecordAction.Position : RecordAction.None;
    }
}
