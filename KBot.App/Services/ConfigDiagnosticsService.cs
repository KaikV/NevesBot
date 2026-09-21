using KBot.App.Models;

namespace KBot.App.Services;

public sealed record DiagnosticItem(string Feature, string Status, string Detail);

public static class ConfigDiagnosticsService
{
    public static List<DiagnosticItem> Diagnose(BotProfile p, NativeStatus? status = null)
    {
        var items = new List<DiagnosticItem>();
        var readerReady = status is { ReaderStatus: "READY", HasPosition: true };

        if (p.AttackerEnabled)
            items.Add(Blocked("Ataque", "A seleção e o movimento estão configurados, mas faltam sinais confiáveis de batalha e alvo do cliente."));
        if (p.AutoReviveEnabled || p.FoodEnabled)
            items.Add(Blocked("Revive / Comida", $"Hotkeys {Safe(p.ReviveItemHotkey)} e {Safe(p.FoodHotkey)} prontas; falta a leitura confiável de HP e estado do Pokémon."));
        if (p.AutoPotion || p.AutoMedicine || p.HealPlayer)
            items.Add(Blocked("Cura", "Prioridades e cooldown configurados; faltam HP do personagem/Pokémon e estado de batalha. O módulo não usa valores estimados."));
        if (p.CatchEnabled)
            items.Add(p.CatchHotkey.Length > 0
                ? Blocked("Captura", $"Hotkey {p.CatchHotkey} e atraso de {p.CatchDelayMs} ms configurados; falta detectar o cadáver com identidade confiável.")
                : Warn("Captura", "Sem hotkey de captura definida no perfil."));
        if (p.LootEnabled)
            items.Add(p.LootHotkey.Length > 0
                ? Ok("Coleta", $"Hotkey {p.LootHotkey} configurada para coleta fora de batalha.")
                : Warn("Coleta", "Sem hotkey de coleta definida no perfil."));
        if (p.FishingEnabled)
            items.Add(Ok("Pesca", $"Ponto ({p.FishingX},{p.FishingY}) com {Safe(p.FishingHotkey)} a cada {p.FishingDelaySeconds}s."));
        if (p.AntiAfkEnabled)
            items.Add(Partial("Anti AFK", $"Passo lateral após {p.AntiAfkIdleSeconds}s parado; a segurança do tile depende da leitura do mapa."));
        if (p.VigiaEnabled)
            items.Add(Partial("Vigia (puxão)", $"Alerta para salto de {p.VigiaDistThreshold}+ tiles; eventos conhecidos do próprio bot são ignorados."));
        if (p.EndgameEnabled)
        {
            var names = new[] { p.EndgameT1Tank, p.EndgameT1D1, p.EndgameT1D2, p.EndgameT2Tank, p.EndgameT2D1, p.EndgameT2D2 };
            var configured = names.Count(n => !string.IsNullOrWhiteSpace(n));
            items.Add(configured == 6
                ? Blocked("End Game (auto combo)", "Rotação 2x3 configurada; troca, moves, cooldown e PokéStop ainda aguardam integração confirmada com o cliente.")
                : Warn("End Game (auto combo)", $"Somente {configured} de 6 bancos estão nomeados; os vazios são pulados e as ações ainda aguardam integração."));
        }
        if (p.AlertsEnabled)
            items.Add(Partial("Alertas", "Histórico operacional local e deduplicação ativos; entrega externa ainda não conectada."));
        if (p.HotkeysEnabled)
            items.Add((p.ManualReviveHotkey.Length > 0 && p.PauseCavebotHotkey.Length > 0 && p.PauseAttackerHotkey.Length > 0)
                ? Ok("Atalhos manuais", $"{Safe(p.ManualReviveHotkey)} / {Safe(p.PauseCavebotHotkey)} / {Safe(p.PauseAttackerHotkey)}.")
                : Warn("Atalhos manuais", "Há atalhos vazios; preencha revive, pausa da rota e pausa do ataque."));

        items.Add(readerReady
            ? Ok("Leitura de memória", $"Posição lida ({status!.PosX},{status.PosY},{status.PosZ}).")
            : Warn("Leitura de memória", "O núcleo não está lendo posição agora. Rode o Caçador de Offset na aba Rota ou verifique o cliente anexado."));

        items.Add(p.MonstersToAttack.Count == 0
            ? Warn("Alvos", "Lista de alvos vazia; adicione ao menos uma criatura.")
            : Ok("Alvos", $"{p.MonstersToAttack.Count} criatura(s) na lista de prioridade."));

        return items;
    }

    private static DiagnosticItem Ok(string feature, string detail) => new(feature, "PRONTO", detail);
    private static DiagnosticItem Warn(string feature, string detail) => new(feature, "ATENÇÃO", detail);
    private static DiagnosticItem Partial(string feature, string detail) => new(feature, "PARCIAL", detail);
    private static DiagnosticItem Blocked(string feature, string detail) => new(feature, "BLOQUEADO", detail);
    private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "(vazio)" : s;
}
