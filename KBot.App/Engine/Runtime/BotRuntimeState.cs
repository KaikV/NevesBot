namespace KBot.App.Engine.Runtime
{
    // The bot's OPERATIONAL memory: what it is doing right now, as opposed to what the world is doing
    // (that is GameStateSnapshot). This is the single mutable state object that survives across ticks.
    // It is deliberately thin and boring: no IO, no clock of its own, cheap to clone, fully unit-testable.
    //
    // Why a separate type from GameStateSnapshot? Three different "states" must not blur together:
    //   GameStateSnapshot  - WHAT the world is, one immutable photo per tick (fused from sensors).
    //   BotRuntimeState    - WHAT the bot is doing: its goal, phase, chosen target, in-flight action.
    //   WorldModel         - WHAT we remember about the world long-term (route, seen rares) - FASE later.
    // Conflating them is exactly how bots silently fight the wrong wild after a target swap, or fire a
    // revive for a foe that was already healed a tick earlier.
    public sealed class BotRuntimeState
    {
        // Who the bot belongs to / which session it is serving. "" until a session is detected.
        public string CurrentOwner { get; set; } = "";

        // The coarse phase the bot is in. Modules read this to know if they even get a say this tick.
        public BotPhase Phase { get; set; } = BotPhase.Idle;

        // The creature the bot committed to acting on. Null when it has not picked one yet or dropped it.
        public TargetRef? Target { get; set; }

        // The action currently in flight while we wait for a confirmation. Null between actions. The
        // ActionManager (FASE G) owns the lifecycle: it installs this, then confirms/aborts/releases.
        public PendingAction? PendingAction { get; set; }

        // A short human-readable line for the dashboard ("alvo Mawile @ (12,4)", "revive em andamento").
        // Never used for logic; purely for observability.
        public string StatusLine { get; set; } = "";

        // Wall-clock ms of the last time the phase/target actually changed. Lets the UI show "ha Xs".
        public long LastChangedAtMs { get; set; }

        // --- transition helpers -------------------------------------------------------------

        // Sets the target and (optionally) bumps the clock. Returns true if it actually changed, so the
        // caller can decide whether to raise a delta. Keeps the old target if the new one is null AND the
        // caller does not want to clear it - see ClearTarget.
        public bool SetTarget(TargetRef? target, long nowMs)
        {
            var changed = !Equals(Target, target);
            Target = target;
            if (changed) LastChangedAtMs = nowMs;
            return changed;
        }

        public void ClearTarget(long nowMs) => SetTarget(null, nowMs);

        // Records that an action started and is now awaiting confirmation. The type (IntentType) tells the
        // confirmer HOW to verify it; the baseline is the world snapshot taken at THIS instant so the check
        // can be "did anything move/leave/appear" rather than a blind sleep timer.
        public void BeginAction(ActionKind kind, string? detail, int requiredRetriesLeft, long nowMs,
            int type = 0, ConfirmBaseline? baseline = null)
        {
            PendingAction = new PendingAction(kind, detail ?? "", requiredRetriesLeft, nowMs, type) { Baseline = baseline };
            LastChangedAtMs = nowMs;
            UpdateStatusLine();
        }

        // Consumes one retry attempt. Returns false when no retries remain -> caller must abort/fallback.
        public bool UseRetry(long nowMs)
        {
            var p = PendingAction;
            if (p is null) return false;
            var left = p.RetriesLeft - 1;
            PendingAction = left <= 0 ? null : new PendingAction(p.Kind, p.Detail, left, p.StartedAtMs, p.Type)
            {
                LastAttemptAtMs = nowMs,
                Baseline = p.Baseline,
            };
            if (left > 0) LastChangedAtMs = nowMs;
            UpdateStatusLine();
            return left > 0;
        }

        // Action finished (confirmed or aborted) - drops it so the next tick can pick a fresh one.
        public void FinishAction(long nowMs)
        {
            if (PendingAction is null) return;
            PendingAction = null;
            LastChangedAtMs = nowMs;
            UpdateStatusLine();
        }

        private void UpdateStatusLine()
        {
            StatusLine = (PendingAction, Target) switch
            {
                ({ } a, { } t) => $"{t.Describe()} · {a.Describe()}",
                ({ } a, null)  => a.Describe(),
                (null, { } t)  => $"alvo: {t.Describe()}",
                _              => "",
            };
        }

        // A pure copy - useful for "what would we decide if nothing changed" tests and for rollback.
        public BotRuntimeState Clone() => new()
        {
            CurrentOwner = CurrentOwner,
            Phase = Phase,
            Target = Target,
            PendingAction = PendingAction is { } p
                ? new PendingAction(p.Kind, p.Detail, p.RetriesLeft, p.StartedAtMs, p.Type)
                {
                    LastAttemptAtMs = p.LastAttemptAtMs,
                    Baseline = p.Baseline,
                }
                : null,
            StatusLine = StatusLine,
            LastChangedAtMs = LastChangedAtMs,
        };
    }

    // Coarse phase of the bot. Intentionally small - fine-grained "what am I doing" lives in modules;
    // this only gates who gets to speak and gives the dashboard an honest one-word summary.
    public enum BotPhase
    {
        Idle,            // out of game / no session yet
        InGame,          // in session, deciding
        Engaged,         // has a target and is actively fighting/catching/looting
        Recovering,      // poke fainted, reviving / swapping
        Stuck,           // cannot make progress (blocked target, dead player, ...)
    }

    // What KIND of in-flight action we are confirming. Ties 1:1 to a confirmation strategy in FASE I;
    // kept as an enum here so the runtime needs no dependency on the action/confirmation layer.
    public enum ActionKind
    {
        None,
        Move,          // walk toward/away from something
        Attack,        // hit the current target
        Catch,         // threw a ball at the current target
        Loot,          // picking up a corpse/item on our tile
        Revive,        // bringing our poke back / healing
        Swap,          // swapping active pokemon
        Chat,          // sent a chat message (rarely confirmed, mostly best-effort)
    }

    // An identity + last-known position for a target, decoupled from any screen scan. We key by whatever
    // id the source offers (native creature id today, screen-cell hash for vision-only), plus the last
    // absolute tile we saw it on, so a stale target can still be judged "out of range" without re-reading.
    public sealed record TargetRef(string Id, int X, int Y, int Z, string Name = "")
    {
        public override string ToString() => Describe();
        public string Describe() => string.IsNullOrWhiteSpace(Name) ? $"#{Id} @ ({X},{Y},{Z})" : $"{Name} @ ({X},{Y},{Z})";
    }

    // The world facts captured AT ACTION START, needed to confirm the action happened afterwards.
    // Honesty: unreadable values stay null/-1 (unknown), they are never filled with 0/false, because
    // "didn't read the scan" must not masquerade as "the screen was empty".
    public sealed record ConfirmBaseline(int? X, int? Y, int? Z, int Wilds, bool? FieldPoke, double? Hp)
    {
        public static ConfirmBaseline Empty => new(null, null, null, -1, null, null);
    }

    // The in-flight action awaiting confirmation. RetriesLeft is the honest counter: 0 means "give up".
    // Type mirrors the IntentType that produced it so the confirmation strategy (FASE I) knows HOW to
    // verify this particular action - a move is confirmed by position change, not by a catch-feed line.
    public sealed record PendingAction(ActionKind Kind, string Detail, int RetriesLeft, long StartedAtMs,
        int Type = 0)
    {
        public long LastAttemptAtMs { get; init; }
        public ConfirmBaseline? Baseline { get; init; }
        public override string ToString() => Describe();
        public string Describe()
        {
            var base_ = Kind switch
            {
                ActionKind.Move => "movendo",
                ActionKind.Attack => "atacando",
                ActionKind.Catch => "capturando",
                ActionKind.Loot => "coletando",
                ActionKind.Revive => "curando/revivendo",
                ActionKind.Swap => "trocando",
                ActionKind.Chat => "chat",
                _ => "agindo",
            };
            return string.IsNullOrWhiteSpace(Detail) ? base_ : $"{base_} {Detail}";
        }
    }
}
