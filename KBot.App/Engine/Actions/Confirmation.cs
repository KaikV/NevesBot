using KBot.App.Engine.Runtime;
using KBot.App.Engine.State;

namespace KBot.App.Engine.Actions
{
    // What the confirmer observed. The loop translates these into manager calls:
    //   NotYet        -> Fail()    (spend a retry, re-fire next tick, until retries run out -> Abort)
    //   Confirmed     -> Confirm() (world proves the effect happened)
    //   Unverifiable  -> Abort()   (this action type has NO sensor evidence in the engine today; we
    //                               release the slot WITHOUT claiming success - honest "done, unverified")
    public enum ConfirmState
    {
        NotYet,
        Confirmed,
        Unverifiable,
    }

    public sealed record ConfirmDecision(ConfirmState State, string Detail)
    {
        public static ConfirmDecision Of(ConfirmState s, string d) => new(s, d);
    }

    /// <summary>
    /// Type-specific confirmation: given the in-flight action (with its at-start baseline) and the
    /// freshest fused snapshot, decide whether the effect is visible in the WORLD. No generic sleep:
    /// every check is a comparison against captured ground truth, so a bot that pressed a key but the
    /// client ignored it reads NotYet and burns its (bounded) retries instead of assuming success.
    /// Pure over (pending, snapshot, now) - trivially headless-testable.
    /// </summary>
    public static class Confirmation
    {
        public static ConfirmDecision Check(PendingAction pending, GameStateSnapshot now, long nowMs)
        {
            var base_ = pending.Baseline;
            switch ((IntentType)pending.Type)
            {
                case IntentType.Move:
                case IntentType.Fish:
                case IntentType.AntiAfk:
                    return CheckMove(pending.Detail, base_, now);

                case IntentType.Attack:
                case IntentType.Catch:
                case IntentType.Revive:
                case IntentType.Swap:
                case IntentType.Summon:
                case IntentType.Recall:
                    return CheckCreatureEvent(base_, now);

                case IntentType.Loot:
                    // Engine has no inventory feed yet: pressing pickup is real, proving the item landed
                    // is not possible today. Release without claiming success.
                    return ConfirmDecision.Of(ConfirmState.Unverifiable, "loot: sem feed de inventario p/ verificar");

                case IntentType.Chat:
                    return ConfirmDecision.Of(ConfirmState.Unverifiable, "chat: fire-and-forget por design");

                default:
                    return ConfirmDecision.Of(FallbackState(now), "tipo sem estrategia de confirmacao");
            }
        }

        private static ConfirmDecision CheckMove(string? what, ConfirmBaseline? base_, GameStateSnapshot now)
        {
            if (base_ is { X: not null } b && now.HasPosition)
            {
                var moved = (now.PosX, now.PosY, now.PosZ) != (b.X, b.Y, b.Z);
                return moved
                    ? ConfirmDecision.Of(ConfirmState.Confirmed, $"posicao mudou ({b.X},{b.Y},{b.Z}) -> ({now.PosX},{now.PosY},{now.PosZ})")
                    : ConfirmDecision.Of(ConfirmState.NotYet, "cliente aceitou o move, mas a posicao ainda nao mudou");
            }
            // No baseline (anchor missing) or fresh position unreadable: we genuinely cannot tell.
            return ConfirmDecision.Of(ConfirmState.NotYet, "posicao nao legivel agora (nao = 'nao moveu')");
        }

        private static ConfirmDecision CheckCreatureEvent(ConfirmBaseline? base_, GameStateSnapshot now)
        {
            if (base_ is { Wilds: >= 0 } b)
            {
                if (now.CreaturesRead)
                {
                    if (now.Wilds.Count != b.Wilds)
                        return ConfirmDecision.Of(ConfirmState.Confirmed, $"campo mudou ({b.Wilds} -> {now.Wilds.Count} criaturas)");
                    if (b.FieldPoke is { } fp && now.FieldHasPoke is { } np && fp != np)
                        return ConfirmDecision.Of(ConfirmState.Confirmed, $"presenca no campo mudou ({fp} -> {np})");
                }
                else
                {
                    // Scan didn't run: absence of evidence, NOT evidence of absence.
                    return ConfirmDecision.Of(ConfirmState.NotYet, "scan de criaturas fora neste tick (nao = 'sem efeito')");
                }
            }
            // No creature baseline was captured (action started with no scan): HP is the only other signal.
            if (base_ is { Hp: not null } hb && now.PlayerHpPercent is { } h)
                return h > hb.Hp! + 1e-6
                    ? ConfirmDecision.Of(ConfirmState.Confirmed, $"HP reagiu ({hb.Hp:F0}% -> {h:F0}%)")
                    : ConfirmDecision.Of(ConfirmState.NotYet, "HP ainda nao subiu");
            return ConfirmDecision.Of(ConfirmState.NotYet, "baseline de criaturas ausente e HP ilegivel");
        }

        private static ConfirmState FallbackState(GameStateSnapshot now) =>
            now.CreaturesRead ? ConfirmState.NotYet : ConfirmState.Unverifiable;
    }
}
