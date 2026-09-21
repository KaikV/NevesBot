using System.Collections.Generic;
using KBot.App.BotBrain;

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

    // Healing & revive (aba Home -> "Healing and revive")
    public bool AutoPotion { get; set; }
    public bool AutoMedicine { get; set; } = true;   // "Curar status (medicine)"
    public bool HealPlayer { get; set; } = true;     // "Curar o jogador"
    public int CureAtPercent { get; set; } = 70;     // threshold to cast heal/medicine
    public string MedicineHotkey { get; set; } = "F11";
    public string HealHotkey { get; set; } = "F12";

    // Targeting (aba Home -> "How the bot attacks" + aba Target)
    public bool AreaCombo { get; set; } = true;      // "Combo em area (varios)"
    public bool AttackOneByOne { get; set; }         // "Attack the target (1 by 1)"
    public bool AutoSummon { get; set; } = true;     // "Soltar poke sozinho"
    public int ActiveSlot { get; set; }              // poke a mandar pra campo (0 = atual)
    public int AttackRange { get; set; } = 7;        // tiles (Chebyshev) que o bot considera "no alcance"
    public bool RareFirst { get; set; } = true;      // shiny/raro na lista tem prioridade total na mira
    public List<string> RareWords { get; set; } = new(); // substrings (ex.: shiny, elite) que viram prioridade
    public bool AlertsEnabled { get; set; }
    // Per-supply alert thresholds (0_AB_catch style "supply" rules): map a bag item
    // key (name or numeric id string, e.g. "2394" pokeball, "3156" revive) to the
    // minimum count below which the falling-edge alert fires. Empty = no supply rule.
    public Dictionary<string, int> SupplyAlerts { get; set; } = new();
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
    // Per-pokemon catch lines (0_AB_catch.lua): map a corpse item id to a ball id.
    // A ball id under 100 means "no real ball bound" and that line never throws.
    public List<KBot.App.BotBrain.CatchEntry> CatchEntries { get; set; } = new();
    public bool CatchShinyEnabled { get; set; }
    public int ShinyBallId { get; set; }
    public bool LootEnabled { get; set; }
    public string LootHotkey { get; set; } = string.Empty;
    public bool PmReplyEnabled { get; set; }
    public string PmPhrases { get; set; } = string.Empty; // CSV of vague replies (see PmResponderService.DefaultPhrases)
    public List<SpellSetting> Spells { get; set; } = new();
}

public sealed class SpellSetting
{
    public string Key { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int CooldownSeconds { get; set; }
}
