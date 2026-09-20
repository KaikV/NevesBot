using System.Collections.Generic;

namespace KBot.App.Models;

public sealed class BotProfile
{
    public List<string> MonstersToAttack { get; set; } = new();
    public bool AttackerEnabled { get; set; }
    public bool AutoReviveEnabled { get; set; }
    public bool FoodEnabled { get; set; }
    public int ReviveHp { get; set; }
    public int ReviveOutOfBattleHp { get; set; }
    public string ReviveItemHotkey { get; set; } = "F9";
    public string FoodHotkey { get; set; } = "F10";
    public bool AlertsEnabled { get; set; }
    public bool HotkeysEnabled { get; set; }
    public string ManualReviveHotkey { get; set; } = string.Empty;
    public string PauseCavebotHotkey { get; set; } = string.Empty;
    public string PauseAttackerHotkey { get; set; } = string.Empty;
    public bool FishingEnabled { get; set; }
    public string FishingHotkey { get; set; } = "Ctrl+Z";
    public int FishingX { get; set; }
    public int FishingY { get; set; }
    public bool CatchEnabled { get; set; }
    public string CatchHotkey { get; set; } = string.Empty;
    public bool LootEnabled { get; set; }
    public string LootHotkey { get; set; } = string.Empty;
    public List<SpellSetting> Spells { get; set; } = new();
}

public sealed class SpellSetting
{
    public string Key { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int CooldownSeconds { get; set; }
}
