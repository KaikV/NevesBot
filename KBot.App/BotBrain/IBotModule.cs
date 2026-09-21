using KBot.App.Models;

namespace KBot.App.BotBrain;

// A feature module (port of one Kryon "nX_"/"panels_*" concern). The Brain ticks
// modules from highest to lowest priority and asks each for its next intent.
// A module returns null when it has nothing to do this tick.
public interface IBotModule
{
    string Name { get; }
    int Priority { get; }
    ActionIntent? Decide(GameState state, IProfileView profile);
}

// Read-only view over BotProfile so modules never mutate settings directly.
public interface IProfileView
{
    // Lets the loop swap in a freshly loaded profile without rebuilding the brain.
    void Refresh(BotProfile profile);

    // Healing & revive
    bool AutoRevive { get; }
    string? ReviveHotkey { get; }
    int ReviveHp { get; }                       // threshold (% hp) to use revive item
    bool AutoPotion { get; }
    string? FoodHotkey { get; }                 // potion/food binding
    bool AutoMedicine { get; }                  // cure status conditions
    string? MedicineHotkey { get; }
    bool HealPlayer { get; }                    // direct hp heal
    string? HealHotkey { get; }
    int CureAtPercent { get; }                   // below this % hp, healing may act

    // Targeting / combat
    bool AttackerEnabled { get; }
    bool AreaCombo { get; }
    bool AttackOneByOne { get; }
    bool AutoSummon { get; }
    int ActiveSlot { get; }                      // poke to send out (0 = keep current)

    // Misc features
    bool FishingEnabled { get; }
    string? FishingHotkey { get; }
    bool CatchEnabled { get; }
    string? CatchHotkey { get; }
    bool LootEnabled { get; }
    string? LootHotkey { get; }
    IReadOnlyList<SpellSettingView> Spells { get; }
}

public sealed record SpellSettingView(string Key, bool Enabled, int CooldownSeconds);
