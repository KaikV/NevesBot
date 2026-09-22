using KBot.App.Engine.Runtime;
using KBot.App.Engine.State;

namespace KBot.App.Engine.Actions
{
    // The resources two intents may fight over. A pending (un-confirmed) action HOLDS these until
    // Confirm() or Abort(); any new intent requiring one of them is blocked until released. This is what
    // stops "walk toward X" from spamming while a catch ball animation is still resolving.
    public enum ActionLock
    {
        Movement,   // WASD / SEND_KEY walk
        Keyboard,   // hotkey press (revive/food/catch/loot all go through a key)
        Mouse,      // right-click pickup / click-to-catch
        Combat,     // the battle context (attack/swap/summon/recall)
        Ui,         // menu open (bag, party screen) - blocks everything else while open
    }

    // WHY the arbiter made its choice. Kept as strings so the dashboard/log can show it verbatim and so
    // tests can assert on the honest reason instead of a magic code.
    public static class BlockReason
    {
        public const string None            = "";
        public const string NothingToDo     = "nothing_to_do";
        public const string Stale           = "stale_state";             // required version newer than we have
        public const string Expired         = "expired";                 // older than its own expiry
        public const string NoFreeLock      = "lock_held";               // wanted a lock someone holds
        public const string AwaitingConfirm = "awaiting_confirmation";   // alias of NoFreeLock (kept for clarity)
    }

    // What the arbiter decided this tick: the single winner (or none) + the honest reason. Exactly ONE
    // intent may win; every other eligible-but-blocked intent explains why in the reason string of the
    // LOSER with the next-highest priority, so the dashboard shows why the bot stood still.
    public sealed record ArbitrationResult(IntentV2? Winner, string Reason)
    {
        public bool HasWinner => Winner is not null;
    }

    // The ONLY place that turns "a bunch of modules all want to act" into "one action". Pure in the
    // sense that given (snapshot, runtime, intents, now) the winner is fully determined - no IO, no RNG.
    //
    // Invariants enforced here (and only here):
    //   1. At most one winner per tick. Higher IntentPriority wins; ties break FIFO by CreatedAtMs,
    //      then SourceModule name so the choice is stable across ticks (no flapping).
    //   2. An intent cannot win while a pending action still holds one of its RequiredLocks.
    //   3. An intent whose RequiredGameStateVersion is newer than the snapshot it was decided against
    //      is STALE and loses - it was computed on older world truth than we now hold.
    //   4. An intent past its ExpiresAtMs is dropped, never executed on stale ground.
    public sealed class ActionManager
    {
        private readonly int _maxRetries;

        public ActionManager(int maxRetries = 2) => _maxRetries = maxRetries;

        // Pick the single winner among ALL intents emitted this tick. Modules have already all run;
        // this is the referee. It installs the winning action into the runtime (so it is "in flight").
        //
        // ONE ACTION AT A TIME is enforced structurally: if one is already in flight, Decide returns
        // AwaitingConfirm and does NOT install a second. The caller (the tick loop / FASE H executor)
        // must Confirm/Fail/Abort the in-flight action before the next tick can start a fresh one. This
        // is what stops "walk toward X" from spamming while a catch ball animation is still resolving.
        public ArbitrationResult Decide(
            GameStateSnapshot snapshot,
            BotRuntimeState runtime,
            IReadOnlyList<IntentV2> intents,
            long nowMs)
        {
            // Already busy? We are awaiting confirmation on it; nothing new may start until resolved.
            var active = runtime.PendingAction;
            if (active is { } a && a.Kind != ActionKind.None)
                return new ArbitrationResult(null, BlockReason.AwaitingConfirm);

            var winner = PickCandidate(intents, snapshot, nowMs, Array.Empty<ActionLock>(), out string reason);
            if (winner is null)
                return new ArbitrationResult(null, intents.Count == 0 ? BlockReason.NothingToDo : reason);

            // Install the winning action so subsequent ticks treat it as in-flight. The baseline is the
            // world truth AT THIS INSTANT: confirmation (FASE I) compares a later snapshot against it to
            // decide "did it actually happen" - no blind sleep timer.
            var kind = MapToActionKind(winner.Type);
            var baseline = new ConfirmBaseline(
                snapshot.PosX, snapshot.PosY, snapshot.PosZ,
                snapshot.CreaturesRead ? snapshot.Wilds.Count : -1,
                snapshot.CreaturesRead ? snapshot.FieldHasPoke : null,
                snapshot.PlayerHpPercent);
            runtime.BeginAction(kind, winner.Detail ?? winner.Payload, _maxRetries, nowMs, (int)winner.Type, baseline);
            if (winner.Type == IntentType.Move || winner.Type == IntentType.Attack)
                runtime.Phase = BotPhase.Engaged;

            return new ArbitrationResult(winner, BlockReason.None);
        }

        // Confirmation landed: the action really happened. Release its locks.
        public void Confirm(BotRuntimeState runtime, long nowMs) => runtime.FinishAction(nowMs);

        // Confirmation failed: spend one retry. Returns true if the action may be RETRIED this/next tick,
        // false if it must be aborted (no retries left) and the winner released.
        public bool Fail(BotRuntimeState runtime, long nowMs)
        {
            if (!runtime.UseRetry(nowMs)) { runtime.FinishAction(nowMs); return false; }
            return true;
        }

        // Give up entirely (target gone, poke fainted mid-catch, ...) without consuming a retry.
        public void Abort(BotRuntimeState runtime, long nowMs) => runtime.FinishAction(nowMs);

        // --- internals ---------------------------------------------------------------

        private static IntentV2? PickCandidate(
            IReadOnlyList<IntentV2> intents,
            GameStateSnapshot snapshot,
            long nowMs,
            ActionLock[] heldLocks,
            out string reason)
        {
            // Highest tier first, then oldest, then stable name. Order is deterministic.
            var ranked = intents
                .Where(i => !i.IsExpired(nowMs))
                .OrderByDescending(i => (int)i.Priority)
                .ThenBy(i => i.CreatedAtMs)
                .ThenBy(i => i.SourceModule, StringComparer.Ordinal)
                .ToList();

            if (ranked.Count == 0) { reason = BlockReason.Expired; return null; }

            // First ranked candidate that is not blocked wins; its block reason (if any) is what we
            // surface - the top-priority reason is the honest one to show, not an average of the rest.
            for (var i = 0; i < ranked.Count; i++)
            {
                var cand = ranked[i];
                if (cand.RequiredGameStateVersion > 0 && cand.RequiredGameStateVersion > snapshot.CapturedAt)
                    continue;   // stale: lose silently, keep looking at lower tiers
                if (Intersects(cand.RequiredLocks, heldLocks))
                    continue;   // lock held: lose silently, keep looking
                reason = BlockReason.None;
                return cand;
            }

            // Everything was blocked. Report why the TOP one lost - that's the honest answer.
            var top = ranked[0];
            reason = top.RequiredGameStateVersion > 0 && top.RequiredGameStateVersion > snapshot.CapturedAt
                ? BlockReason.Stale
                : BlockReason.NoFreeLock;
            return null;
        }

        private static bool Intersects(IReadOnlyCollection<ActionLock> wanted, ActionLock[] held)
        {
            if (held.Length == 0) return false;
            if (wanted.Count == 0) return false;
            var heldSet = new HashSet<ActionLock>(held);
            foreach (var w in wanted) if (heldSet.Contains(w)) return true;
            return false;
        }

        private static ActionKind MapToActionKind(IntentType t) => t switch
        {
            IntentType.Move    => ActionKind.Move,
            IntentType.Attack  => ActionKind.Attack,
            IntentType.Catch   => ActionKind.Catch,
            IntentType.Loot    => ActionKind.Loot,
            IntentType.Revive  => ActionKind.Revive,
            IntentType.Swap    => ActionKind.Swap,
            IntentType.Summon  => ActionKind.Swap,
            IntentType.Recall  => ActionKind.Swap,
            IntentType.Fish    => ActionKind.Move,
            IntentType.Chat    => ActionKind.Chat,
            IntentType.AntiAfk => ActionKind.Move,
            _                  => ActionKind.Move,
        };

        // Which locks a pending action of a given kind holds while it awaits confirmation. This is the
        // single source of truth for "what am I busy with" - keep it in step with MapToActionKind.
        public static ActionLock[] HeldLocks(ActionKind kind) => kind switch
        {
            ActionKind.Move    => new[] { ActionLock.Movement },
            ActionKind.Attack  => new[] { ActionLock.Combat, ActionLock.Keyboard },
            ActionKind.Catch   => new[] { ActionLock.Mouse, ActionLock.Keyboard },
            ActionKind.Loot    => new[] { ActionLock.Mouse },
            ActionKind.Revive  => new[] { ActionLock.Combat, ActionLock.Keyboard },
            ActionKind.Swap    => new[] { ActionLock.Combat },
            ActionKind.Chat    => new[] { ActionLock.Keyboard },
            _                  => Array.Empty<ActionLock>(),
        };
    }
}
