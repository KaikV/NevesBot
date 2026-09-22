using System.Collections.Generic;
using KBot.App.BotBrain;

namespace KBot.App.Models;

public sealed class BotProfile
{
    public int SchemaVersion { get; set; } = 2;
    // One-shot KryonBot-style auto defaults: profiles saved before version 1 had no
    // explicit choice yet, so Load() turns the safe modules ON once and stamps 1.
    // Endgame stays OFF (it needs the six poke names configured first).
    public int AutoDefaultsVersion { get; set; }
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
    public int PlayerHealPercent { get; set; } = 80;
    public int HealingCooldownMs { get; set; } = 1500;
    public bool HealOnlyOutOfBattle { get; set; }

    // Targeting (aba Home -> "How the bot attacks" + aba Target)
    public bool AreaCombo { get; set; } = true;      // "Combo em area (varios)"
    public bool AttackOneByOne { get; set; }         // "Attack the target (1 by 1)"
    public bool AutoSummon { get; set; } = true;     // "Soltar poke sozinho"
    public int ActiveSlot { get; set; }              // poke a mandar pra campo (0 = atual)
    public int AttackRange { get; set; } = 7;        // tiles (Chebyshev) que o bot considera "no alcance"
    public bool RareFirst { get; set; } = true;      // shiny/raro na lista tem prioridade total na mira
    public List<string> RareWords { get; set; } = new() { "shiny", "elite", "ancient" }; // substrings que viram prioridade
    public List<string> IgnoredMonsters { get; set; } = new();
    public bool TargetMoveEnabled { get; set; } = true;
    public bool TargetApproachEnabled { get; set; } = true;
    public bool TargetFollowInBattle { get; set; } = true;
    public int TargetMoveIntervalMs { get; set; } = 2500;
    public int TargetKeepDistance { get; set; } = 1;
    public bool PauseRouteOnTarget { get; set; }
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
    // Seconds between casts (0_AD_fish.lua). 0 = default 13s (the server answer rhythm).
    public int FishingDelaySeconds { get; set; } = 13;
    // Max wild pokemons nearby before fishing pauses (so you can fight them). -1 = never pause.
    public int FishingMaxPoke { get; set; } = -1;
    // Radius (tiles) to scan for the nearest water tile. Clamped to [1,12] at runtime.
    public int FishingRaio { get; set; } = 7;
    // Server item id of the fishing-spot object on the ground (Lua default 48415).
    public int FishingWaterId { get; set; } = 48415;
    public int FishingX { get; set; }
    public int FishingY { get; set; }
    public bool CatchEnabled { get; set; }
    public string CatchHotkey { get; set; } = string.Empty;
    public int CatchDelayMs { get; set; } = 200;
    // Per-pokemon catch lines (0_AB_catch.lua): map a corpse item id to a ball id.
    // A ball id under 100 means "no real ball bound" and that line never throws.
    public List<KBot.App.BotBrain.CatchEntry> CatchEntries { get; set; } = new();
    public bool CatchShinyEnabled { get; set; }
    public int ShinyBallId { get; set; }
    public bool LootEnabled { get; set; }
    public string LootHotkey { get; set; } = string.Empty;
    // Anti-AFK (nL_antiafk.lua): a single sideways step when standing still, with the
    // step-back scheduled right after. Off by default - most users move their char anyway.
    public bool AntiAfkEnabled { get; set; }
    // Seconds standing on one tile before the nudge (clamped to >= 15 in Normalize).
    public int AntiAfkIdleSeconds { get; set; } = 50;
    // Vigia / puxao (nF8_vigia.lua): alarm when a GM drags the character. ON by default -
    // "alarme mudo e o pior defeito que um alarme pode ter" (nascido ligado no Lua).
    public bool VigiaEnabled { get; set; } = true;
    // Tiles of jump that count as a pull (clamped to >= 2 in Normalize). Default 3.
    public int VigiaDistThreshold { get; set; } = 3;
    // End Game / Auto Combo (main_endgame.lua): 2x3 rotation (1 tank + 2 area-damage)
    // that farms big waves WITHOUT revive - the swap is the cooldown reset. Off by
    // default: the names below have to point at real pokebar slots first.
    public bool EndgameEnabled { get; set; }
    public string EndgameT1Tank { get; set; } = string.Empty;
    public string EndgameT1D1 { get; set; } = string.Empty;
    public string EndgameT1D2 { get; set; } = string.Empty;
    public string EndgameT2Tank { get; set; } = string.Empty;
    public string EndgameT2D1 { get; set; } = string.Empty;
    public string EndgameT2D2 { get; set; } = string.Empty;
    public int EndgameWaveCount { get; set; } = 8;      // wilds that must gather before the combo
    public int EndgameRingTiles { get; set; } = 1;     // chebyshev ring around the tank counted as "glued"
    public int EndgameSeeStop { get; set; } = 8;       // wilds on screen before the tank stops pulling
    public int EndgameStopDist { get; set; } = 1;      // nearest-wild distance that also stops the pull
    public int EndgameApproachSqm { get; set; } = 1;   // how close to the pile before the full combo
    public int EndgameRelureS { get; set; } = 20;      // stalled-gather seconds before re-pulling (0 = wait forever)
    public int EndgameMoveGapMs { get; set; } = 180;   // cadence between combo moves (Lua default)
    public int EndgamePotItem { get; set; }            // potion item id (0 = no potion)
    public int EndgamePotPct { get; set; } = 99;       // pot the tank below this %
    public int EndgameSavePct { get; set; } = 40;      // mid-combo rescue potion threshold
    public int EndgameSwapPct { get; set; } = 15;     // below this % pull the tank out early (swap)
    public bool EndgameUseSafe { get; set; }          // walk the safe spot between teams
    public int EndgameSafeReach { get; set; } = 1;    // tiles around the safe spot still counting as arrived
    public int EndgameRecoverMaxS { get; set; } = 30; // max seconds waiting on in-ball cd
    public int EndgameReburst { get; set; } = 5;      // damage re-combo cap while kit still has moves
    public bool EndgamePokeStop { get; set; } = true; // speak !pokestop for damage pokes
    public string EndgamePokeStopCmd { get; set; } = "!pokestop";
    public int EndgameSafeX { get; set; }             // (0,0) = no safe spot -> recover in place
    public int EndgameSafeY { get; set; }
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
