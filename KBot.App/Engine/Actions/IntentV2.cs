using KBot.App.BotBrain;
using KBot.App.Engine.Runtime;

namespace KBot.App.Engine.Actions
{
    // Coarse priority tier used for arbitration. Two intents NEVER share a winner: the higher tier wins,
    // regardless of who asked. Ties inside a tier fall back to "older first" (FIFO), then source name.
    public enum IntentPriority
    {
        Utility        = 10,   // anti-afk wiggle, chat pings, cosmetic things
        Movement       = 50,   // walking toward/away/along route
        CatchLoot      = 70,   // throwing a ball / picking up a corpse
        Combat         = 80,   // attacking the current target
        CriticalCombat = 90,   // emergency attack: wild ON US, poke about to faint
        Safety         = 100,  // revive / swap-out / run away - always wins
    }

    // What this intent WANTS to do. Kept independent of how it is realised (hotkey vs SEND_KEY vs a
    // future native call) so the same intent can run on any execution path. Channel+Payload mirror the
    // legacy ActionIntent so a ToLegacy() bridge exists for the dual-run migration window.
    public enum IntentType
    {
        None,
        Move,
        Attack,
        Catch,
        Loot,
        Revive,
        Swap,
        Summon,     // send a pokemon out
        Recall,     // call it back in
        Fish,
        Chat,
        AntiAfk,
    }

    // A desired effect, stamped with enough metadata that the ActionManager can arbitrate WITHOUT
    // asking the module anything. Modules stay dumb; the arbiter reads these fields only.
    public sealed record IntentV2
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public required string SourceModule { get; init; }
        public required IntentType Type { get; init; }
        public required IntentPriority Priority { get; init; }
        public long CreatedAtMs { get; init; }
        public long ExpiresAtMs { get; init; }                       // <= CreatedAtMs == "no expiry"
        public long RequiredGameStateVersion { get; init; }          // 0 == do not check (compat)
        public IReadOnlyCollection<ActionLock> RequiredLocks { get; init; } = Array.Empty<ActionLock>();

        // Realisation hint (legacy bridge). Null until an executor picks the concrete payload.
        public ActionChannel? Channel { get; init; }
        public string? Payload { get; init; }

        // Human detail shown in the dashboard ("Mawile @ (12,4,3)", "slot 2", direction, ...).
        public string? Detail { get; init; }

        public bool IsExpired(long nowMs) => ExpiresAtMs > 0 && nowMs > ExpiresAtMs;

        public override string ToString() =>
            $"{SourceModule}/{Type}@{(int)Priority} {Detail}".TrimEnd();

        // Bridge back to the legacy shape so the OLD executor (BrainActionSink) can run it during the
        // dual-run migration window. Modules only learn to emit V2 gradually; until they do, the brain
        // wraps their legacy intents into this form.
        public ActionIntent ToLegacy() => Type switch
        {
            IntentType.Move    => ActionIntent.Move(Payload ?? ""),
            IntentType.Catch   => ActionIntent.Hotkey(Payload ?? "catch"),
            IntentType.Loot    => ActionIntent.Hotkey(Payload ?? "loot"),
            IntentType.Revive  => ActionIntent.Hotkey(Payload ?? "revive"),
            IntentType.Swap    => ActionIntent.Command("swap", Payload),
            IntentType.Summon  => ActionIntent.Command("summon", Payload),
            IntentType.Recall  => ActionIntent.Command("recall", Payload),
            IntentType.Fish    => ActionIntent.Hotkey(Payload ?? "fish"),
            IntentType.Chat    => ActionIntent.Command("chat", Payload),
            IntentType.AntiAfk => ActionIntent.Command("antiafk"),
            _                  => ActionIntent.Command($"intent:{Type}", Payload),
        };
    }

    // Convert a legacy intent (still emitted by un-migrated modules) into a V2 one. The caller stamps
    // the timestamp; the priority/locks come from a simple type-based default the arbiter can override
    // later with per-module overrides if a feature needs a custom tier.
    public static class IntentBridge
    {
        public static IntentV2 FromLegacy(ActionIntent legacy, string sourceModule, long nowMs, IntentType? hint = null)
        {
            IntentType type;
            IntentPriority priority;
            ActionLock[] locks;
            if (hint is { } h)
            {
                // The caller knows what the intent means (it made it) - trust that over guessing.
                type = h; priority = PriorityForType(h); locks = LocksForType(h);
            }
            else switch (legacy.Channel)
            {
                case ActionChannel.Move:
                    type = IntentType.Move; priority = IntentPriority.Movement; locks = new[] { ActionLock.Movement }; break;
                case ActionChannel.Hotkey:
                    type = GuessHotkeyType(legacy.Payload); priority = GuessHotkeyPriority(legacy.Payload); locks = GuessHotkeyLocks(legacy.Payload); break;
                default:
                    type = GuessCommandType(legacy.Payload); priority = GuessCommandPriority(legacy.Payload); locks = GuessCommandLocks(legacy.Payload); break;
            }
            return new IntentV2
            {
                SourceModule = sourceModule,
                Type = type,
                Priority = priority,
                CreatedAtMs = nowMs,
                RequiredLocks = locks,
                Channel = legacy.Channel,
                Payload = legacy.Payload,
            };
        }

        // Canonical tier + locks per semantic type - the single table the brain uses when a module tells
        // us what its intent is. (Guess* below is only the fallback when no hint is available.)
        private static IntentPriority PriorityForType(IntentType t) => t switch
        {
            IntentType.Move    => IntentPriority.Movement,
            IntentType.Attack  => IntentPriority.Combat,
            IntentType.Catch   => IntentPriority.CatchLoot,
            IntentType.Loot    => IntentPriority.CatchLoot,
            IntentType.Revive  => IntentPriority.Safety,
            IntentType.Swap    => IntentPriority.Safety,
            IntentType.Summon  => IntentPriority.Combat,
            IntentType.Recall  => IntentPriority.Combat,
            IntentType.Fish    => IntentPriority.Movement,
            IntentType.Chat    => IntentPriority.Utility,
            IntentType.AntiAfk => IntentPriority.Utility,
            _                  => IntentPriority.Utility,
        };

        private static ActionLock[] LocksForType(IntentType t) => t switch
        {
            IntentType.Move     => new[] { ActionLock.Movement },
            IntentType.Attack   => new[] { ActionLock.Combat, ActionLock.Keyboard },
            IntentType.Catch    => new[] { ActionLock.Mouse, ActionLock.Keyboard },
            IntentType.Loot     => new[] { ActionLock.Mouse },
            IntentType.Revive   => new[] { ActionLock.Combat, ActionLock.Keyboard },
            IntentType.Swap     => new[] { ActionLock.Combat },
            IntentType.Summon   => new[] { ActionLock.Combat },
            IntentType.Recall   => new[] { ActionLock.Combat },
            IntentType.Fish     => new[] { ActionLock.Movement },
            IntentType.Chat     => new[] { ActionLock.Keyboard },
            IntentType.AntiAfk  => new[] { ActionLock.Movement },
            _                   => Array.Empty<ActionLock>(),
        };

        private static IntentType GuessHotkeyType(string? p)
        {
            var k = (p ?? "").ToLowerInvariant();
            if (k.Contains("revive")) return IntentType.Revive;
            if (k.Contains("food") || k.Contains("potion") || k.Contains("heal")) return IntentType.Revive;
            if (k.Contains("catch") || k.Contains("ball")) return IntentType.Catch;
            if (k.Contains("loot") || k.Contains("pickup")) return IntentType.Loot;
            if (k.Contains("fish") || k.Contains("rod")) return IntentType.Fish;
            return IntentType.Attack;
        }
        private static IntentPriority GuessHotkeyPriority(string? p)
        {
            var k = (p ?? "").ToLowerInvariant();
            if (k.Contains("revive") || k.Contains("food") || k.Contains("potion") || k.Contains("heal"))
                return IntentPriority.Safety;
            if (k.Contains("catch") || k.Contains("loot")) return IntentPriority.CatchLoot;
            return IntentPriority.Combat;
        }
        private static ActionLock[] GuessHotkeyLocks(string? p)
        {
            var k = (p ?? "").ToLowerInvariant();
            if (k.Contains("revive") || k.Contains("swap")) return new[] { ActionLock.Combat, ActionLock.Keyboard };
            if (k.Contains("catch") || k.Contains("loot")) return new[] { ActionLock.Mouse, ActionLock.Keyboard };
            return new[] { ActionLock.Keyboard };
        }
        private static IntentType GuessCommandType(string? p)
        {
            var k = (p ?? "").ToLowerInvariant().Split(':')[0];
            return k switch
            {
                "swap"   => IntentType.Swap,
                "summon" => IntentType.Summon,
                "recall" => IntentType.Recall,
                "chat"   => IntentType.Chat,
                "antiafk"=> IntentType.AntiAfk,
                _        => IntentType.Attack,
            };
        }
        private static IntentPriority GuessCommandPriority(string? p)
        {
            var k = (p ?? "").ToLowerInvariant().Split(':')[0];
            return k switch
            {
                "swap" => IntentPriority.Safety,
                "summon" or "recall" => IntentPriority.Combat,
                "chat" or "antiafk"  => IntentPriority.Utility,
                _ => IntentPriority.Combat,
            };
        }
        private static ActionLock[] GuessCommandLocks(string? p)
        {
            var k = (p ?? "").ToLowerInvariant().Split(':')[0];
            return k switch
            {
                "swap" or "summon" or "recall" => new[] { ActionLock.Combat, ActionLock.Keyboard },
                "chat" or "antiafk"            => new[] { ActionLock.Keyboard },
                _                              => new[] { ActionLock.Keyboard },
            };
        }
    }
}
