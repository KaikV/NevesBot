namespace KBot.App.BotBrain;

// Port of n9_socorro.lua + the "Healing and revive" Home panel.
// Priority 100: survival outruns everything.
//
// Rules ported (with graceful degradation while HP/active offsets are pending):
//   * active fainted  -> use the revive item FIRST (revive beats send-out).
//   * hp < threshold  -> medicine (cure status) then heal player, in that order.
//   * no readable hp  -> modules skip; they must never fire on a guessed value.
public sealed class HealingModule : IBotModule
{
    public string Name => "Cura";
    public int Priority => 100;

    // A healed/cured poke shouldn't be re-cast every tick.
    private long _lastCureAtMs;

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        // 1) Revive: only meaningful when we KNOW our pokemon is down.
        if (s.ActiveAlive == false && p.AutoRevive && !string.IsNullOrWhiteSpace(p.ReviveHotkey))
            return ActionIntent.Hotkey(p.ReviveHotkey);

        if (p.HealOnlyOutOfBattle && s.InBattle != false) return null;

        var pokemonHp = s.ActiveHpPercent;
        var playerHp = s.PlayerHpPercent;

        // 2) Cure status / medicine.
        if (p.AutoMedicine && !string.IsNullOrWhiteSpace(p.MedicineHotkey) &&
            pokemonHp is { } v && v < p.CureAtPercent && CooldownOk(s.NowMs, p.HealingCooldownMs))
        {
            _lastCureAtMs = s.NowMs;
            return ActionIntent.Hotkey(p.MedicineHotkey);
        }

        // 3) Heal player hp.
        if (p.HealPlayer && !string.IsNullOrWhiteSpace(p.HealHotkey) &&
            playerHp is { } w && w < p.PlayerHealPercent && CooldownOk(s.NowMs, p.HealingCooldownMs))
        {
            _lastCureAtMs = s.NowMs;
            return ActionIntent.Hotkey(p.HealHotkey);
        }

        // 4) Potion/food is lower priority than medicine+heal; handled last.
        if (p.AutoPotion && !string.IsNullOrWhiteSpace(p.FoodHotkey) &&
            pokemonHp is { } x && x < p.CureAtPercent && CooldownOk(s.NowMs, p.HealingCooldownMs))
        {
            _lastCureAtMs = s.NowMs;
            return ActionIntent.Hotkey(p.FoodHotkey);
        }

        return null;
    }

    private bool CooldownOk(long now, int configuredMs) =>
        now - _lastCureAtMs >= Math.Clamp(configuredMs, 250, 60_000);

    internal void ResetTimers() => _lastCureAtMs = 0;
}
