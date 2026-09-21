namespace KBot.App.BotBrain;

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

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!p.AttackerEnabled) return null;

        // In battle: choose the attack shape from the profile.
        if (s.InBattle == true)
        {
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
            var target = TargetSelection.Pick(
                s.Wilds, s.X, s.Y, s.Z, p.AttackRange, p.RareFirst, p.RareWords);
            if (target is not null)
                return ActionIntent.Command("aim", target.Name);
        }

        // Not in battle: if auto-summon is on and we know the field is empty
        // (active pokemon down), request sending one out. Slot 0 = keep/auto.
        if (p.AutoSummon && s.ActiveAlive == false)
            return ActionIntent.Command("summon", p.ActiveSlot.ToString());

        return null;
    }
}
