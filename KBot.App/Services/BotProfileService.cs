using KBot.App.BotBrain;
using KBot.App.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KBot.App.Services;

public static class BotProfileService
{
    public static string ProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KBot", "profile.json");

    public static BotProfile Load()
    {
        if (!File.Exists(ProfilePath)) return Normalize(new BotProfile());
        return Parse(File.ReadAllText(ProfilePath));
    }

    public static void Save(BotProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        File.WriteAllText(ProfilePath, Serialize(profile));
    }

    public static BotProfile Parse(string json) =>
        Normalize(JsonSerializer.Deserialize<BotProfile>(json) ?? throw new InvalidDataException("Perfil KBot vazio."));

    public static string Serialize(BotProfile profile) =>
        JsonSerializer.Serialize(Normalize(profile), new JsonSerializerOptions { WriteIndented = true });

    public static BotProfile Import(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            (TryGet(root, nameof(BotProfile.AutoReviveEnabled), out _) ||
             TryGet(root, nameof(BotProfile.FishingEnabled), out _)))
            return Parse(json);
        return ImportLegacy(json);
    }

    public static BotProfile ImportLegacy(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0) throw new InvalidDataException("Perfil vazio.");
            root = root[0];
        }
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Perfil inválido.");
        if (!TryGet(root, "Hotkeys", out _) && !TryGet(root, "Revive", out _) &&
            !TryGet(root, "Spells", out _) && !TryGet(root, "MonstersToAttack", out _))
            throw new InvalidDataException("O arquivo não contém opções de perfil reconhecidas.");

        var profile = new BotProfile();
        if (TryGet(root, "MonstersToAttack", out var monsters) && monsters.ValueKind == JsonValueKind.Array)
            profile.MonstersToAttack = monsters.EnumerateArray()
                .Where(m => m.ValueKind == JsonValueKind.String)
                .Select(m => m.GetString() ?? string.Empty)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

        profile.AlertsEnabled = ReadBool(root, "Alarms", "enabled");
        profile.HotkeysEnabled = ReadBool(root, "Hotkeys", "enabled");
        profile.ManualReviveHotkey = ReadString(root, "Hotkeys", "ReviveHotkey");
        profile.PauseCavebotHotkey = ReadString(root, "Hotkeys", "PauseCavebotHotkey");
        profile.PauseAttackerHotkey = ReadString(root, "Hotkeys", "PauseAttackerHotkey");
        profile.AutoReviveEnabled = ReadBool(root, "Revive", "enabled");
        profile.ReviveHp = ReadInt(root, "Revive", "AutoReviveHP");
        profile.ReviveOutOfBattleHp = ReadInt(root, "Revive", "AutoReviveOutOfBattleHP");
        profile.ReviveItemHotkey = NormalizeHotkey(ReadString(root, "Revive", "ReviveItemHotkey"), "F9");
        profile.FoodHotkey = NormalizeHotkey(ReadString(root, "Food", "FoodHotkey"), "F10");
        profile.FoodEnabled = TryGetNested(root, "Food", "FoodHotkey", out var foodHotkey) &&
            foodHotkey.ValueKind == JsonValueKind.String &&
            !string.Equals(profile.FoodHotkey, "Disabled", StringComparison.OrdinalIgnoreCase);

        if (TryGet(root, "Spells", out var spells) && spells.ValueKind == JsonValueKind.Object)
        {
            for (int i = 1; i <= 9; i++)
            {
                string key = $"F{i}";
                profile.Spells.Add(new SpellSetting
                {
                    Key = key,
                    Enabled = ReadBool(spells, key, "enabled"),
                    CooldownSeconds = ReadInt(spells, key, "cooldown")
                });
            }
        }
        return Normalize(profile);
    }

    public static BotProfile Normalize(BotProfile profile)
    {
        profile.MonstersToAttack ??= new List<string>();
        profile.Spells ??= new List<SpellSetting>();
        profile.Spells = Enumerable.Range(1, 9).Select(i =>
        {
            string key = $"F{i}";
            var spell = profile.Spells.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
            return new SpellSetting
            {
                Key = key,
                Enabled = spell?.Enabled ?? false,
                CooldownSeconds = Math.Max(0, spell?.CooldownSeconds ?? 0)
            };
        }).ToList();
        profile.ReviveHp = Math.Max(0, profile.ReviveHp);
        profile.ReviveOutOfBattleHp = Math.Max(0, profile.ReviveOutOfBattleHp);
        profile.ReviveItemHotkey = NormalizeHotkey(profile.ReviveItemHotkey, "F9");
        profile.FoodHotkey = NormalizeHotkey(profile.FoodHotkey, "F10");
        profile.FishingHotkey = NormalizeHotkey(profile.FishingHotkey, "Ctrl+Z");
        profile.FishingX = Math.Max(0, profile.FishingX);
        profile.FishingY = Math.Max(0, profile.FishingY);
        profile.FishingRaio = Math.Clamp(profile.FishingRaio, 1, 12);
        profile.FishingDelaySeconds = Math.Max(1, profile.FishingDelaySeconds);
        profile.CatchHotkey = profile.CatchHotkey?.Trim() ?? string.Empty;
        profile.LootHotkey = profile.LootHotkey?.Trim() ?? string.Empty;
        // 15 = AntiAfkTracker.IdleMinSeconds (kept as a literal: the Models->BotBrain
        // reference direction is not allowed here).
        profile.AntiAfkIdleSeconds = Math.Max(15, profile.AntiAfkIdleSeconds);
        // 2 = the floor VigiaTracker applies (a 1-tile jump is a step, never a pull).
        profile.VigiaDistThreshold = Math.Max(2, profile.VigiaDistThreshold);
        // Endgame knobs (literals, same style as above): counts are at least 1,
        // the potion item is 0 (off) or a real server id (>= 100), percentages 0..100.
        profile.EndgameT1Tank = profile.EndgameT1Tank?.Trim() ?? string.Empty;
        profile.EndgameT1D1 = profile.EndgameT1D1?.Trim() ?? string.Empty;
        profile.EndgameT1D2 = profile.EndgameT1D2?.Trim() ?? string.Empty;
        profile.EndgameT2Tank = profile.EndgameT2Tank?.Trim() ?? string.Empty;
        profile.EndgameT2D1 = profile.EndgameT2D1?.Trim() ?? string.Empty;
        profile.EndgameT2D2 = profile.EndgameT2D2?.Trim() ?? string.Empty;
        profile.EndgameWaveCount = Math.Max(1, profile.EndgameWaveCount);
        profile.EndgameRingTiles = Math.Max(1, profile.EndgameRingTiles);
        profile.EndgameSeeStop = Math.Max(1, profile.EndgameSeeStop);
        profile.EndgameStopDist = Math.Max(1, profile.EndgameStopDist);
        profile.EndgameApproachSqm = Math.Max(1, profile.EndgameApproachSqm);
        profile.EndgameRelureS = Math.Max(0, profile.EndgameRelureS);
        profile.EndgameMoveGapMs = Math.Max(60, profile.EndgameMoveGapMs);
        profile.EndgamePotItem = profile.EndgamePotItem >= 100 ? profile.EndgamePotItem : 0;
        profile.EndgamePotPct = Math.Clamp(profile.EndgamePotPct, 1, 100);
        profile.EndgameSavePct = Math.Clamp(profile.EndgameSavePct, 0, 100);
        profile.EndgameSwapPct = Math.Clamp(profile.EndgameSwapPct, 0, 100);
        profile.EndgameSafeReach = Math.Max(0, profile.EndgameSafeReach);
        profile.EndgameRecoverMaxS = Math.Max(0, profile.EndgameRecoverMaxS);
        profile.EndgameReburst = Math.Max(0, profile.EndgameReburst);
        profile.EndgamePokeStopCmd = profile.EndgamePokeStopCmd?.Trim() ?? "!pokestop";
        profile.EndgameSafeX = Math.Max(0, profile.EndgameSafeX);
        profile.EndgameSafeY = Math.Max(0, profile.EndgameSafeY);
        return profile;
    }

    private static string NormalizeHotkey(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().Trim('{', '}');

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static bool TryGetNested(JsonElement root, string section, string name, out JsonElement value)
    {
        if (TryGet(root, section, out var group)) return TryGet(group, name, out value);
        value = default;
        return false;
    }

    private static bool ReadBool(JsonElement root, string section, string name) =>
        TryGetNested(root, section, name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement root, string section, string name) =>
        TryGetNested(root, section, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? Math.Max(0, number) : 0;

    private static string ReadString(JsonElement root, string section, string name) =>
        TryGetNested(root, section, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
}
