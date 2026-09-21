using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KBot.App.Models;

namespace KBot.App.Services;

public sealed class ConfigShareResult
{
    public required string Message { get; init; }
    public int Applied { get; init; }
    public int Ignored { get; init; }
}public static class ConfigShareService
{
    private const string Version = "KPB1";
    private const char Escape = '%';

    private sealed record Field(string Code, Func<BotProfile, object?> Read, Action<BotProfile, object> Write);

    private static readonly List<Field> Fields = new()
    {
        new("at", p => p.AttackerEnabled, (p, v) => p.AttackerEnabled = (bool)v),
        new("ac", p => p.AreaCombo, (p, v) => p.AreaCombo = (bool)v),
        new("ob", p => p.AttackOneByOne, (p, v) => p.AttackOneByOne = (bool)v),
        new("as", p => p.AutoSummon, (p, v) => p.AutoSummon = (bool)v),
        new("sl", p => p.ActiveSlot, (p, v) => p.ActiveSlot = (int)v),
        new("rv", p => p.AutoReviveEnabled, (p, v) => p.AutoReviveEnabled = (bool)v),
        new("fh", p => p.FoodEnabled, (p, v) => p.FoodEnabled = (bool)v),
        new("rh", p => p.ReviveHp, (p, v) => p.ReviveHp = (int)v),
        new("oh", p => p.ReviveOutOfBattleHp, (p, v) => p.ReviveOutOfBattleHp = (int)v),
        new("rk", p => p.ReviveItemHotkey, (p, v) => p.ReviveItemHotkey = (string?)v ?? ""),
        new("fk", p => p.FoodHotkey, (p, v) => p.FoodHotkey = (string?)v ?? ""),
        new("ap", p => p.AutoPotion, (p, v) => p.AutoPotion = (bool)v),
        new("am", p => p.AutoMedicine, (p, v) => p.AutoMedicine = (bool)v),
        new("hp", p => p.HealPlayer, (p, v) => p.HealPlayer = (bool)v),
        new("cp", p => p.CureAtPercent, (p, v) => p.CureAtPercent = (int)v),
        new("mk", p => p.MedicineHotkey, (p, v) => p.MedicineHotkey = (string?)v ?? ""),
        new("hk", p => p.HealHotkey, (p, v) => p.HealHotkey = (string?)v ?? ""),
        new("al", p => p.AlertsEnabled, (p, v) => p.AlertsEnabled = (bool)v),
        new("hk2", p => p.HotkeysEnabled, (p, v) => p.HotkeysEnabled = (bool)v),
        new("mr", p => p.ManualReviveHotkey, (p, v) => p.ManualReviveHotkey = (string?)v ?? ""),
        new("pc", p => p.PauseCavebotHotkey, (p, v) => p.PauseCavebotHotkey = (string?)v ?? ""),
        new("pa", p => p.PauseAttackerHotkey, (p, v) => p.PauseAttackerHotkey = (string?)v ?? ""),
        new("fe", p => p.FishingEnabled, (p, v) => p.FishingEnabled = (bool)v),
        new("fko", p => p.FishingHotkey, (p, v) => p.FishingHotkey = (string?)v ?? ""),
        new("fx", p => p.FishingX, (p, v) => p.FishingX = (int)v),
        new("fy", p => p.FishingY, (p, v) => p.FishingY = (int)v),
        new("fds", p => p.FishingDelaySeconds, (p, v) => p.FishingDelaySeconds = (int)v),
        new("fmp", p => p.FishingMaxPoke, (p, v) => p.FishingMaxPoke = (int)v),
        new("fra", p => p.FishingRaio, (p, v) => p.FishingRaio = (int)v),
        new("fwi", p => p.FishingWaterId, (p, v) => p.FishingWaterId = (int)v),
        new("ce2", p => p.CatchEnabled, (p, v) => p.CatchEnabled = (bool)v),
        new("ck", p => p.CatchHotkey, (p, v) => p.CatchHotkey = (string?)v ?? ""),
        new("le", p => p.LootEnabled, (p, v) => p.LootEnabled = (bool)v),
        new("lk", p => p.LootHotkey, (p, v) => p.LootHotkey = (string?)v ?? ""),
        new("ae", p => p.AntiAfkEnabled, (p, v) => p.AntiAfkEnabled = (bool)v),
        new("ai", p => p.AntiAfkIdleSeconds, (p, v) => p.AntiAfkIdleSeconds = (int)v),
        new("ve", p => p.VigiaEnabled, (p, v) => p.VigiaEnabled = (bool)v),
        new("vd", p => p.VigiaDistThreshold, (p, v) => p.VigiaDistThreshold = (int)v),
        new("ge", p => p.EndgameEnabled, (p, v) => p.EndgameEnabled = (bool)v),
        new("gn1", p => p.EndgameT1Tank, (p, v) => p.EndgameT1Tank = (string?)v ?? ""),
        new("gn2", p => p.EndgameT1D1, (p, v) => p.EndgameT1D1 = (string?)v ?? ""),
        new("gn3", p => p.EndgameT1D2, (p, v) => p.EndgameT1D2 = (string?)v ?? ""),
        new("gn4", p => p.EndgameT2Tank, (p, v) => p.EndgameT2Tank = (string?)v ?? ""),
        new("gn5", p => p.EndgameT2D1, (p, v) => p.EndgameT2D1 = (string?)v ?? ""),
        new("gn6", p => p.EndgameT2D2, (p, v) => p.EndgameT2D2 = (string?)v ?? ""),
        new("gw", p => p.EndgameWaveCount, (p, v) => p.EndgameWaveCount = (int)v),
        new("gr", p => p.EndgameRingTiles, (p, v) => p.EndgameRingTiles = (int)v),
        new("gs", p => p.EndgameSeeStop, (p, v) => p.EndgameSeeStop = (int)v),
        new("gd", p => p.EndgameStopDist, (p, v) => p.EndgameStopDist = (int)v),
        new("ga", p => p.EndgameApproachSqm, (p, v) => p.EndgameApproachSqm = (int)v),
        new("gl", p => p.EndgameRelureS, (p, v) => p.EndgameRelureS = (int)v),
        new("gg", p => p.EndgameMoveGapMs, (p, v) => p.EndgameMoveGapMs = (int)v),
        new("gi", p => p.EndgamePotItem, (p, v) => p.EndgamePotItem = (int)v),
        new("gq", p => p.EndgamePotPct, (p, v) => p.EndgamePotPct = (int)v),
        new("gv", p => p.EndgameSavePct, (p, v) => p.EndgameSavePct = (int)v),
        new("gx", p => p.EndgameSwapPct, (p, v) => p.EndgameSwapPct = (int)v),
        new("gf", p => p.EndgameUseSafe, (p, v) => p.EndgameUseSafe = (bool)v),
        new("gc", p => p.EndgameSafeReach, (p, v) => p.EndgameSafeReach = (int)v),
        new("gm", p => p.EndgameRecoverMaxS, (p, v) => p.EndgameRecoverMaxS = (int)v),
        new("gb", p => p.EndgameReburst, (p, v) => p.EndgameReburst = (int)v),
        new("gz", p => p.EndgamePokeStop, (p, v) => p.EndgamePokeStop = (bool)v),
        new("gt", p => p.EndgamePokeStopCmd, (p, v) => p.EndgamePokeStopCmd = (string?)v ?? ""),
        new("gsx", p => p.EndgameSafeX, (p, v) => p.EndgameSafeX = (int)v),
        new("gsy", p => p.EndgameSafeY, (p, v) => p.EndgameSafeY = (int)v),
    };

    public static string Export(BotProfile profile)
    {
        var parts = new List<string>();
        foreach (var field in Fields.OrderBy(f => f.Code, StringComparer.Ordinal))
        {
            var value = field.Read(profile);
            if (value is bool b) parts.Add($"{field.Code}=b{(b ? "1" : "0")}");
            else if (value is int n) parts.Add($"{field.Code}=n{n}");
            else if (value is string s) parts.Add($"{field.Code}=s{EscapeValue(s)}");
        }
        var monsters = profile.MonstersToAttack.Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => EscapeValue(m.Trim())).ToList();
        if (monsters.Count > 0) parts.Add("ml=" + string.Join("~", monsters));

        var body = string.Join(";", parts);
        return $"{Version}:{body}:{Checksum(body)}";
    }

    public static ConfigShareResult Import(string code, BotProfile into)
    {
        var clean = string.Concat(code?.Where(c => !char.IsWhiteSpace(c)) ?? Enumerable.Empty<char>());
        if (clean.Length == 0) return new ConfigShareResult { Message = "Código vazio." };

        var parts = clean.Split(':');
        if (parts.Length != 3)
        {
            if (clean.StartsWith("KPB", StringComparison.OrdinalIgnoreCase))
                return new ConfigShareResult { Message = "O código chegou CORTADO. Copie a linha inteira (do KPB até o fim)." };
            return new ConfigShareResult { Message = $"Isso não parece um código do KBot (o certo começa com {Version}:)." };
        }

        if (!parts[0].Equals(Version, StringComparison.Ordinal))
            return new ConfigShareResult { Message = $"Código da versão {parts[0]}; este KBot lê {Version}. Peça um código novo." };

        var body = parts[1];
        if (Checksum(body) != parts[2])
            return new ConfigShareResult { Message = "Código incompleto ou alterado (a verificação não bate). Copie a linha inteira." };

        var pending = new List<Action>();
        var ignored = 0;
        List<string>? monsters = null;
        var tokens = body.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var eq = token.IndexOf('=');
            if (eq <= 0 || eq == token.Length - 1) { ignored++; continue; }
            var codePart = token[..eq];
            var payload = token[(eq + 1)..];
            if (payload.Length < 2) { ignored++; continue; }
            var tag = payload[0];
            var data = payload[1..];

            if (codePart == "ml")
            {
                monsters = payload.Split('~', StringSplitOptions.RemoveEmptyEntries)
                    .Select(UnescapeValue).Where(m => m.Length > 0).ToList();
                continue;
            }

            var field = Fields.FirstOrDefault(f => f.Code == codePart);
            if (field is null) { ignored++; continue; }

            object? parsed = tag switch
            {
                'b' => data == "1",
                'n' => int.TryParse(data, out var n) ? n : null,
                's' => UnescapeValue(data),
                _ => null
            };
            if (parsed is null && tag == 's') parsed = "";
            if (parsed is null) { ignored++; continue; }

            var write = field.Write;
            var value = parsed;
            pending.Add(() => write(into, value));
        }

        foreach (var apply in pending) apply();
        if (monsters is not null && monsters.Count > 0)
            into.MonstersToAttack = monsters;

        return new ConfigShareResult
        {
            Applied = pending.Count + (monsters is not null ? 1 : 0),
            Ignored = ignored,
            Message = ignored > 0
                ? $"{pending.Count + (monsters is not null ? 1 : 0)} ajustes aplicados, {ignored} ignorados."
                : $"{pending.Count + (monsters is not null ? 1 : 0)} ajustes aplicados.",
        };
    }

    private static string EscapeValue(string value)
    {
        if (value.IndexOfAny(new[] { '%', ';', ':', '|', '~', ' ', '=', '\n', '\r', '\t' }) < 0) return value;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(char.IsWhiteSpace(c) || c is '%' or ';' or ':' or '|' or '~' or '=' ? $"%{((int)c):X2}" : c);
        return sb.ToString();
    }

    private static string UnescapeValue(string value)
    {
        if (value.Contains('%'))
        {
            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '%' && i + 2 < value.Length && IsHex(value[i + 1]) && IsHex(value[i + 2]))
                {
                    sb.Append((char)(HexVal(value[i + 1]) * 16 + HexVal(value[i + 2])));
                    i += 2;
                }
                else sb.Append(value[i]);
            }
            return sb.ToString();
        }
        return value;
    }

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'A' and <= 'F';
    private static int HexVal(char c) => c <= '9' ? c - '0' : c - 'A' + 10;

    private static string Checksum(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        uint a = 1, bsum = 0;
        foreach (var x in bytes)
        {
            a = (a + x) % 65521;
            bsum = (bsum + a) % 65521;
        }
        return ((bsum << 16) | a).ToString("X");
    }
}
