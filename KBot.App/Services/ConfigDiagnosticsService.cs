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
            items.Add(Ok("Ataque", "Offset de batalha ainda não mapeado: decisão de alvo por lista de nomes só (nome certo + hotkey do cliente)."));
        if (p.AutoReviveEnabled || p.FoodEnabled)
            items.Add(Ok("Revive / Comida", $"Usa hotkeys {Safe(p.ReviveItemHotkey)} e {Safe(p.FoodHotkey)} — depende de teclas configuradas no cliente. Offset de HP/estado não mapeado."));
        if (p.AutoPotion || p.AutoMedicine || p.HealPlayer)
            items.Add(Ok("Cura", $"Limiar {p.CureAtPercent}% com {Safe(p.MedicineHotkey)}/{Safe(p.HealHotkey)} — precisa de offset de HP pra disparar sozinho."));
        if (p.CatchEnabled)
            items.Add(p.CatchHotkey.Length > 0
                ? Ok("Captura", $"Hotkey {p.CatchHotkey} configurada. Disparo em batalha funciona quando estado de batalha for mapeado.")
                : Warn("Captura", "Sem hotkey de captura definida no perfil."));
        if (p.LootEnabled)
            items.Add(p.LootHotkey.Length > 0
                ? Ok("Coleta", $"Hotkey {p.LootHotkey} configurada para coleta fora de batalha.")
                : Warn("Coleta", "Sem hotkey de coleta definida no perfil."));
        if (p.FishingEnabled)
            items.Add(Ok("Pesca", $"Ponto ({p.FishingX},{p.FishingY}) com {Safe(p.FishingHotkey)} — o módulo lança a vara no ponto mais próximo a cada {p.FishingDelaySeconds}s, no ritmo do servidor."));
        if (p.AlertsEnabled)
            items.Add(Ok("Alertas", "Sinal de puxão/GM detectado pelo brain; entrega por Telegram ainda não conectada."));
        if (p.HotkeysEnabled)
            items.Add((p.ManualReviveHotkey.Length > 0 && p.PauseCavebotHotkey.Length > 0 && p.PauseAttackerHotkey.Length > 0)
                ? Ok("Atalhos manuais", $"{Safe(p.ManualReviveHotkey)} / {Safe(p.PauseCavebotHotkey)} / {Safe(p.PauseAttackerHotkey)}.")
                : Warn("Atalhos manuais", "Há atalhos vazios — preencha revive/pausa cavebot/pausa ataque."));

        items.Add(readerReady
            ? Ok("Leitura de memória", $"Posição lida ({status!.PosX},{status.PosY},{status.PosZ}).")
            : Warn("Leitura de memória", "Núcleo não está lendo posição agora. Rode o Caçador de Offset (aba Rota) ou verifique o cliente anexado."));

        items.Add(p.MonstersToAttack.Count == 0
            ? Warn("Alvos", "Lista de alvos vazia — adicione ao menos uma criatura.")
            : Ok("Alvos", $"{p.MonstersToAttack.Count} criatura(s) na lista de prioridade."));

        return items;
    }

    private static DiagnosticItem Ok(string feature, string detail) => new(feature, "PRONTO", detail);
    private static DiagnosticItem Warn(string feature, string detail) => new(feature, "ATENÇÃO", detail);
    private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "(vazio)" : s;
}
