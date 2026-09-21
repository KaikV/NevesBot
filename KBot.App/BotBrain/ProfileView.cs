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
    public int FishingDelaySeconds => _p.FishingDelaySeconds;
    public int FishingMaxPoke => _p.FishingMaxPoke;
    public int FishingRaio => _p.FishingRaio;
    public bool AlertsEnabled => _p.AlertsEnabled;
    public IReadOnlyDictionary<string, int> SupplyAlerts => _p.SupplyAlerts;
    public bool CatchEnabled => _p.CatchEnabled;
    public string? CatchHotkey => _p.CatchHotkey;
    public IReadOnlyList<CatchEntry> CatchEntries => _p.CatchEntries;
    public bool CatchShinyEnabled => _p.CatchShinyEnabled;
    public int ShinyBallId => _p.ShinyBallId;
    public bool LootEnabled => _p.LootEnabled;
    public string? LootHotkey => _p.LootHotkey;
    public bool AntiAfkEnabled => _p.AntiAfkEnabled;
    public int AntiAfkIdleSeconds => _p.AntiAfkIdleSeconds;
    public bool VigiaEnabled => _p.VigiaEnabled;
    public int VigiaDistThreshold => _p.VigiaDistThreshold;
    public bool EndgameEnabled => _p.EndgameEnabled;
    public string EndgameT1Tank => _p.EndgameT1Tank;
    public string EndgameT1D1 => _p.EndgameT1D1;
    public string EndgameT1D2 => _p.EndgameT1D2;
    public string EndgameT2Tank => _p.EndgameT2Tank;
    public string EndgameT2D1 => _p.EndgameT2D1;
    public string EndgameT2D2 => _p.EndgameT2D2;
    public int EndgameWaveCount => _p.EndgameWaveCount;
    public int EndgameRingTiles => _p.EndgameRingTiles;
    public int EndgameSeeStop => _p.EndgameSeeStop;
    public int EndgameStopDist => _p.EndgameStopDist;
    public int EndgameApproachSqm => _p.EndgameApproachSqm;
    public int EndgameRelureS => _p.EndgameRelureS;
    public int EndgameMoveGapMs => _p.EndgameMoveGapMs;
    public int EndgamePotItem => _p.EndgamePotItem;
    public int EndgamePotPct => _p.EndgamePotPct;
    public int EndgameSavePct => _p.EndgameSavePct;
    public int EndgameSwapPct => _p.EndgameSwapPct;
    public bool EndgameUseSafe => _p.EndgameUseSafe;
    public int EndgameSafeReach => _p.EndgameSafeReach;
    public int EndgameRecoverMaxS => _p.EndgameRecoverMaxS;
    public int EndgameReburst => _p.EndgameReburst;
    public bool EndgamePokeStop => _p.EndgamePokeStop;
    public string EndgamePokeStopCmd => _p.EndgamePokeStopCmd;
    public int EndgameSafeX => _p.EndgameSafeX;
    public int EndgameSafeY => _p.EndgameSafeY;
    public IReadOnlyList<SpellSettingView> Spells =>
        _p.Spells.Where(s => !string.IsNullOrWhiteSpace(s.Key))
                 .Select(s => new SpellSettingView(s.Key, s.Enabled, s.CooldownSeconds)).ToList();
}
