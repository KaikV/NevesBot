namespace KBot.App.BotBrain;

using KBot.App.Services;

// Port of n1_combate.lua (attack mode) + n2_lure.lua (positioning).
// Priority 90: fights after survival.
//
// What is actionable TODAY (keyboard/vision only, battle-state offset pending):
//   * AutoSummon - send a poke out when field is empty. Pressed as a Command
//     until we confirm the "send out slot N" binding; the Brain logs it.
//   * In-battle attack - emits a Command ("attack:combo" or "attack:single").
//     Real cast needs the skill hotkeys (m1..m12) mapped in the client; until
//     then the intent is a no-op placeholder so the decision flow is ported.
// One-by-one vs area-combo mirrors the Home tab toggle directly.
public sealed class TargetingModule : IBotModule
{
    public string Name => "Target";
    public int Priority => 90;

    private string? _lockedKey;
    private long _lastMoveMs = -60_000;

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!p.AttackerEnabled)
        {
            if (_lockedKey is not null)
                AutomationEventHub.Shared.Publish(AutomationEventSeverity.Info, "Target", "target_cleared", "Seleção de alvo desligada.");
            _lockedKey = null;
            return null;
        }

        // In battle: choose the attack shape from the profile.
        if (s.InBattle == true)
        {
            var locked = FindLocked(s);
            if (p.TargetFollowInBattle && p.TargetMoveEnabled && p.TargetApproachEnabled && locked is not null &&
                TryMoveToward(s, p, locked, out var movement))
                return movement;
            return p.AttackOneByOne
                ? ActionIntent.Command("attack", "single")
                : ActionIntent.Command("attack", p.AreaCombo ? "combo" : "single");
        }

        // Out of battle, with a readable screen: pick WHICH wild to aim at. Rare/shiny
        // words take total priority, otherwise the closest in-range same-floor wild.
        // The selection itself is ported + tested in TargetSelection; the aim is a
        // pending command until the "target creature N" offset exists.
        if (s.HasPosition && s.Wilds.Count > 0)
        {
            var target = FindLocked(s) ?? TargetSelection.Pick(
                s.Wilds, s.X, s.Y, s.Z, p.AttackRange, p.RareFirst, p.RareWords,
                p.MonstersToAttack, p.IgnoredMonsters);
            if (target is not null)
            {
                var key = Key(target);
                if (!string.Equals(_lockedKey, key, StringComparison.Ordinal))
                {
                    _lockedKey = key;
                    AutomationEventHub.Shared.Publish(AutomationEventSeverity.Info, "Target", "target_acquired",
                        $"Alvo travado: {target.Name}.", key);
                    return ActionIntent.Command("aim", target.Name);
                }

                if (p.TargetMoveEnabled && p.TargetApproachEnabled && TryMoveToward(s, p, target, out var movement))
                    return movement;
                return null;
            }
        }
        else if (s.HasScreenScan)
        {
            if (_lockedKey is not null)
                AutomationEventHub.Shared.Publish(AutomationEventSeverity.Info, "Target", "target_lost", "Alvo saiu da leitura.", _lockedKey);
            _lockedKey = null;
        }

        // Not in battle: if auto-summon is on and we know the field is empty
        // (active pokemon down), request sending one out. Slot 0 = keep/auto.
        if (p.AutoSummon && s.ActiveAlive == false)
            return ActionIntent.Command("summon", p.ActiveSlot.ToString());

        return null;
    }

    private ScannedCreature? FindLocked(GameState s)
    {
        if (_lockedKey is null) return null;
        foreach (var wild in s.Wilds)
            if (Key(wild) == _lockedKey && wild.IsAlive && wild.Z == s.Z &&
                Math.Max(Math.Abs(wild.X - s.X), Math.Abs(wild.Y - s.Y)) <= 20)
                return wild;
        return null;
    }

    private static string Key(ScannedCreature creature) =>
        $"{creature.Name.ToUpperInvariant()}|{creature.X}|{creature.Y}|{creature.Z}";

    private bool TryMoveToward(GameState s, IProfileView p, ScannedCreature target, out ActionIntent? movement)
    {
        movement = null;
        var distance = Math.Max(Math.Abs(target.X - s.X), Math.Abs(target.Y - s.Y));
        if (distance <= p.TargetKeepDistance || s.NowMs - _lastMoveMs < p.TargetMoveIntervalMs) return false;
        _lastMoveMs = s.NowMs;
        var direction = Math.Abs(target.X - s.X) >= Math.Abs(target.Y - s.Y)
            ? (target.X > s.X ? "D" : "A")
            : (target.Y > s.Y ? "S" : "W");
        movement = ActionIntent.Move(direction);
        return true;
    }
}
