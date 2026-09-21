using KBot.App.Models;

namespace KBot.App.BotBrain;

// Maps the app's BotProfile into the read-only view modules consume. Lives here
// so modules never depend on the UI-facing model.
public sealed class ProfileView : IProfileView
{
    private BotProfile _p;
    public ProfileView(BotProfile p) => _p = p;

    // The lifecycle reloads the on-disk profile every tick so edits made in the
    // settings tabs apply without restarting the client.
    public void Refresh(BotProfile p) => _p = p;

    public bool AutoRevive => _p.AutoReviveEnabled;
    public string? ReviveHotkey => _p.ReviveItemHotkey;
    public int ReviveHp => _p.ReviveHp;
    public bool AutoPotion => _p.AutoPotion;
    public string? FoodHotkey => _p.FoodHotkey;
    public bool AutoMedicine => _p.AutoMedicine;
    public bool HealPlayer => _p.HealPlayer;
    public int CureAtPercent => _p.CureAtPercent;
    public string MedicineHotkey => _p.MedicineHotkey;
    public string HealHotkey => _p.HealHotkey;
    public bool AttackerEnabled => _p.AttackerEnabled;
    public bool AreaCombo => _p.AreaCombo;
    public bool AttackOneByOne => _p.AttackOneByOne;
    public bool AutoSummon => _p.AutoSummon;
    public int ActiveSlot => _p.ActiveSlot;
    public int AttackRange => _p.AttackRange;
    public bool RareFirst => _p.RareFirst;
    public IReadOnlyList<string> RareWords => _p.RareWords;
    public IReadOnlyList<string> MonstersToAttack => _p.MonstersToAttack;
    public bool FishingEnabled => _p.FishingEnabled;
    public string? FishingHotkey => _p.FishingHotkey;
    public bool AlertsEnabled => _p.AlertsEnabled;
    public IReadOnlyDictionary<string, int> SupplyAlerts => _p.SupplyAlerts;
    public bool CatchEnabled => _p.CatchEnabled;
    public string? CatchHotkey => _p.CatchHotkey;
    public IReadOnlyList<CatchEntry> CatchEntries => _p.CatchEntries;
    public bool CatchShinyEnabled => _p.CatchShinyEnabled;
    public int ShinyBallId => _p.ShinyBallId;
    public bool LootEnabled => _p.LootEnabled;
    public string? LootHotkey => _p.LootHotkey;
    public IReadOnlyList<SpellSettingView> Spells =>
        _p.Spells.Where(s => !string.IsNullOrWhiteSpace(s.Key))
                 .Select(s => new SpellSettingView(s.Key, s.Enabled, s.CooldownSeconds)).ToList();
}
