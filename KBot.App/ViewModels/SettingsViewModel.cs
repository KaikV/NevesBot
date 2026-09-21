using KBot.App.Models;
using KBot.App.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace KBot.App.ViewModels;

public sealed class SpellEditor : ObservableObject
{
    private bool _enabled;
    private string _cooldownText = "0";
    public string Key { get; init; } = string.Empty;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string CooldownText { get => _cooldownText; set => Set(ref _cooldownText, value); }
}

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private string _newMonster = string.Empty;
    private string? _selectedMonster;
    private bool _attackerEnabled;
    private bool _areaCombo = true;
    private bool _attackOneByOne;
    private bool _autoSummon = true;
    private int _activeSlot;
    private string _attackRange = "7";
    private string _ignoredMonsters = string.Empty;
    private string _rareWords = "shiny, elite, ancient";
    private bool _rareFirst = true;
    private bool _targetMoveEnabled = true;
    private bool _targetApproachEnabled = true;
    private bool _targetFollowInBattle = true;
    private string _targetMoveIntervalMs = "2500";
    private string _targetKeepDistance = "1";
    private bool _pauseRouteOnTarget;
    private bool _autoReviveEnabled;
    private bool _foodEnabled;
    private bool _autoPotion;
    private bool _autoMedicine = true;
    private bool _healPlayer = true;
    private int _cureAtPercent = 70;
    private string _playerHealPercent = "80";
    private string _healingCooldownMs = "1500";
    private bool _healOnlyOutOfBattle;
    private string _reviveHp = "0";
    private string _reviveOutOfBattleHp = "0";
    private string _reviveItemHotkey = "F9";
    private string _foodHotkey = "F10";
    private string _medicineHotkey = "F11";
    private string _healHotkey = "F12";
    private bool _alertsEnabled;
    private bool _hotkeysEnabled;
    private string _manualReviveHotkey = string.Empty;
    private string _pauseCavebotHotkey = string.Empty;
    private string _pauseAttackerHotkey = string.Empty;
    private bool _fishingEnabled;
    private string _fishingHotkey = "Ctrl+Z";
    private string _fishingX = "0";
    private string _fishingY = "0";
    private string _fishingDelaySeconds = "13";
    private string _fishingMaxPoke = "-1";
    private string _fishingRaio = "7";
    private string _fishingWaterId = "48415";
    private bool _catchEnabled;
    private string _catchHotkey = string.Empty;
    private string _catchDelayMs = "200";
    private bool _catchShinyEnabled;
    private string _shinyBallId = "0";
    private string _catchRules = string.Empty;
    private bool _lootEnabled;
    private string _lootHotkey = string.Empty;
    private bool _antiAfkEnabled;
    private string _antiAfkIdleSeconds = "50";
    private bool _vigiaEnabled = true;
    private string _vigiaDistThreshold = "3";
    private bool _endgameEnabled;
    private string _endgameT1Tank = string.Empty;
    private string _endgameT1D1 = string.Empty;
    private string _endgameT1D2 = string.Empty;
    private string _endgameT2Tank = string.Empty;
    private string _endgameT2D1 = string.Empty;
    private string _endgameT2D2 = string.Empty;
    private string _endgameWaveCount = "8";
    private string _endgameRingTiles = "1";
    private string _endgameSeeStop = "8";
    private string _endgameStopDist = "1";
    private string _endgameApproachSqm = "1";
    private string _endgameRelureS = "20";
    private string _endgameMoveGapMs = "180";
    private string _endgamePotItem = "0";
    private string _endgamePotPct = "99";
    private string _endgameSavePct = "40";
    private string _endgameSwapPct = "15";
    private bool _endgameUseSafe;
    private string _endgameSafeReach = "1";
    private string _endgameRecoverMaxS = "30";
    private string _endgameReburst = "5";
    private bool _endgamePokeStop = true;
    private string _endgamePokeStopCmd = "!pokestop";
    private string _endgameSafeX = "0";
    private string _endgameSafeY = "0";
    private bool _pmReplyEnabled;
    private string _pmPhrases = string.Empty;
    private readonly NativeService _nativeService = new();
    private string _feedback = "Configurações locais. Nenhuma automação será iniciada nesta tela.";

    public ObservableCollection<string> Monsters { get; } = new();
    public ObservableCollection<SpellEditor> Spells { get; } = new();
    public ObservableCollection<KBot.App.Services.DiagnosticItem> Diagnostics { get; } = new();
    public ObservableCollection<AutomationEvent> AlertHistory { get; } = new();
    public string NewMonster { get => _newMonster; set => Set(ref _newMonster, value); }
    public string? SelectedMonster { get => _selectedMonster; set { Set(ref _selectedMonster, value); OnPropertyChanged(nameof(HasSelectedMonster)); } }
    public bool HasSelectedMonster => SelectedMonster is not null;
    public bool AttackerEnabled { get => _attackerEnabled; set => Set(ref _attackerEnabled, value); }
    public bool AreaCombo { get => _areaCombo; set => Set(ref _areaCombo, value); }
    public bool AttackOneByOne { get => _attackOneByOne; set => Set(ref _attackOneByOne, value); }
    public bool AutoSummon { get => _autoSummon; set => Set(ref _autoSummon, value); }
    public int ActiveSlot { get => _activeSlot; set => Set(ref _activeSlot, value); }
    public string AttackRange { get => _attackRange; set => Set(ref _attackRange, value); }
    public string IgnoredMonsters { get => _ignoredMonsters; set => Set(ref _ignoredMonsters, value); }
    public string RareWords { get => _rareWords; set => Set(ref _rareWords, value); }
    public bool RareFirst { get => _rareFirst; set => Set(ref _rareFirst, value); }
    public bool TargetMoveEnabled { get => _targetMoveEnabled; set => Set(ref _targetMoveEnabled, value); }
    public bool TargetApproachEnabled { get => _targetApproachEnabled; set => Set(ref _targetApproachEnabled, value); }
    public bool TargetFollowInBattle { get => _targetFollowInBattle; set => Set(ref _targetFollowInBattle, value); }
    public string TargetMoveIntervalMs { get => _targetMoveIntervalMs; set => Set(ref _targetMoveIntervalMs, value); }
    public string TargetKeepDistance { get => _targetKeepDistance; set => Set(ref _targetKeepDistance, value); }
    public bool PauseRouteOnTarget { get => _pauseRouteOnTarget; set => Set(ref _pauseRouteOnTarget, value); }
    public bool AutoReviveEnabled { get => _autoReviveEnabled; set => Set(ref _autoReviveEnabled, value); }
    public bool FoodEnabled { get => _foodEnabled; set => Set(ref _foodEnabled, value); }
    public bool AutoPotion { get => _autoPotion; set => Set(ref _autoPotion, value); }
    public bool AutoMedicine { get => _autoMedicine; set => Set(ref _autoMedicine, value); }
    public bool HealPlayer { get => _healPlayer; set => Set(ref _healPlayer, value); }
    public int CureAtPercent { get => _cureAtPercent; set => Set(ref _cureAtPercent, value); }
    public string PlayerHealPercent { get => _playerHealPercent; set => Set(ref _playerHealPercent, value); }
    public string HealingCooldownMs { get => _healingCooldownMs; set => Set(ref _healingCooldownMs, value); }
    public bool HealOnlyOutOfBattle { get => _healOnlyOutOfBattle; set => Set(ref _healOnlyOutOfBattle, value); }
    public string ReviveHp { get => _reviveHp; set => Set(ref _reviveHp, value); }
    public string ReviveOutOfBattleHp { get => _reviveOutOfBattleHp; set => Set(ref _reviveOutOfBattleHp, value); }
    public string ReviveItemHotkey { get => _reviveItemHotkey; set => Set(ref _reviveItemHotkey, value); }
    public string FoodHotkey { get => _foodHotkey; set => Set(ref _foodHotkey, value); }
    public string MedicineHotkey { get => _medicineHotkey; set => Set(ref _medicineHotkey, value); }
    public string HealHotkey { get => _healHotkey; set => Set(ref _healHotkey, value); }
    public bool AlertsEnabled { get => _alertsEnabled; set => Set(ref _alertsEnabled, value); }
    public bool HotkeysEnabled { get => _hotkeysEnabled; set => Set(ref _hotkeysEnabled, value); }
    public string ManualReviveHotkey { get => _manualReviveHotkey; set => Set(ref _manualReviveHotkey, value); }
    public string PauseCavebotHotkey { get => _pauseCavebotHotkey; set => Set(ref _pauseCavebotHotkey, value); }
    public string PauseAttackerHotkey { get => _pauseAttackerHotkey; set => Set(ref _pauseAttackerHotkey, value); }
    public bool FishingEnabled { get => _fishingEnabled; set => Set(ref _fishingEnabled, value); }
    public string FishingHotkey { get => _fishingHotkey; set => Set(ref _fishingHotkey, value); }
    public string FishingX { get => _fishingX; set => Set(ref _fishingX, value); }
    public string FishingY { get => _fishingY; set => Set(ref _fishingY, value); }
    public string FishingDelaySeconds { get => _fishingDelaySeconds; set => Set(ref _fishingDelaySeconds, value); }
    public string FishingMaxPoke { get => _fishingMaxPoke; set => Set(ref _fishingMaxPoke, value); }
    public string FishingRaio { get => _fishingRaio; set => Set(ref _fishingRaio, value); }
    public string FishingWaterId { get => _fishingWaterId; set => Set(ref _fishingWaterId, value); }
    public bool CatchEnabled { get => _catchEnabled; set => Set(ref _catchEnabled, value); }
    public string CatchHotkey { get => _catchHotkey; set => Set(ref _catchHotkey, value); }
    public string CatchDelayMs { get => _catchDelayMs; set => Set(ref _catchDelayMs, value); }
    public bool CatchShinyEnabled { get => _catchShinyEnabled; set => Set(ref _catchShinyEnabled, value); }
    public string ShinyBallId { get => _shinyBallId; set => Set(ref _shinyBallId, value); }
    public string CatchRules { get => _catchRules; set => Set(ref _catchRules, value); }
    public bool LootEnabled { get => _lootEnabled; set => Set(ref _lootEnabled, value); }
    public string LootHotkey { get => _lootHotkey; set => Set(ref _lootHotkey, value); }
    public bool AntiAfkEnabled { get => _antiAfkEnabled; set => Set(ref _antiAfkEnabled, value); }
    public string AntiAfkIdleSeconds { get => _antiAfkIdleSeconds; set => Set(ref _antiAfkIdleSeconds, value); }
    public bool VigiaEnabled { get => _vigiaEnabled; set => Set(ref _vigiaEnabled, value); }
    public string VigiaDistThreshold { get => _vigiaDistThreshold; set => Set(ref _vigiaDistThreshold, value); }
    public bool EndgameEnabled { get => _endgameEnabled; set => Set(ref _endgameEnabled, value); }
    public string EndgameT1Tank { get => _endgameT1Tank; set => Set(ref _endgameT1Tank, value); }
    public string EndgameT1D1 { get => _endgameT1D1; set => Set(ref _endgameT1D1, value); }
    public string EndgameT1D2 { get => _endgameT1D2; set => Set(ref _endgameT1D2, value); }
    public string EndgameT2Tank { get => _endgameT2Tank; set => Set(ref _endgameT2Tank, value); }
    public string EndgameT2D1 { get => _endgameT2D1; set => Set(ref _endgameT2D1, value); }
    public string EndgameT2D2 { get => _endgameT2D2; set => Set(ref _endgameT2D2, value); }
    public string EndgameWaveCount { get => _endgameWaveCount; set => Set(ref _endgameWaveCount, value); }
    public string EndgameRingTiles { get => _endgameRingTiles; set => Set(ref _endgameRingTiles, value); }
    public string EndgameSeeStop { get => _endgameSeeStop; set => Set(ref _endgameSeeStop, value); }
    public string EndgameStopDist { get => _endgameStopDist; set => Set(ref _endgameStopDist, value); }
    public string EndgameApproachSqm { get => _endgameApproachSqm; set => Set(ref _endgameApproachSqm, value); }
    public string EndgameRelureS { get => _endgameRelureS; set => Set(ref _endgameRelureS, value); }
    public string EndgameMoveGapMs { get => _endgameMoveGapMs; set => Set(ref _endgameMoveGapMs, value); }
    public string EndgamePotItem { get => _endgamePotItem; set => Set(ref _endgamePotItem, value); }
    public string EndgamePotPct { get => _endgamePotPct; set => Set(ref _endgamePotPct, value); }
    public string EndgameSavePct { get => _endgameSavePct; set => Set(ref _endgameSavePct, value); }
    public string EndgameSwapPct { get => _endgameSwapPct; set => Set(ref _endgameSwapPct, value); }
    public bool EndgameUseSafe { get => _endgameUseSafe; set => Set(ref _endgameUseSafe, value); }
    public string EndgameSafeReach { get => _endgameSafeReach; set => Set(ref _endgameSafeReach, value); }
    public string EndgameRecoverMaxS { get => _endgameRecoverMaxS; set => Set(ref _endgameRecoverMaxS, value); }
    public string EndgameReburst { get => _endgameReburst; set => Set(ref _endgameReburst, value); }
    public bool EndgamePokeStop { get => _endgamePokeStop; set => Set(ref _endgamePokeStop, value); }
    public string EndgamePokeStopCmd { get => _endgamePokeStopCmd; set => Set(ref _endgamePokeStopCmd, value); }
    public string EndgameSafeX { get => _endgameSafeX; set => Set(ref _endgameSafeX, value); }
    public string EndgameSafeY { get => _endgameSafeY; set => Set(ref _endgameSafeY, value); }
    public bool PmReplyEnabled { get => _pmReplyEnabled; set => Set(ref _pmReplyEnabled, value); }
    public string PmPhrases { get => _pmPhrases; set => Set(ref _pmPhrases, value); }
    public string Feedback { get => _feedback; private set => Set(ref _feedback, value); }

    public ICommand ImportCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ExportCodeCommand { get; }
    public ICommand ImportCodeCommand { get; }
    public ICommand DiagnoseCommand { get; }
    public ICommand AddMonsterCommand { get; }
    public ICommand RemoveMonsterCommand { get; }
    public ICommand MoveMonsterUpCommand { get; }
    public ICommand MoveMonsterDownCommand { get; }
    public ICommand ClearAlertHistoryCommand { get; }

    public SettingsViewModel()
    {
        ImportCommand = new RelayCommand(_ => Import());
        SaveCommand = new RelayCommand(_ => Save());
        ExportCodeCommand = new RelayCommand(_ => ExportCode());
        ImportCodeCommand = new RelayCommand(_ => ImportCode());
        DiagnoseCommand = new RelayCommand(_ => _ = DiagnoseAsync());
        AddMonsterCommand = new RelayCommand(_ => AddMonster());
        RemoveMonsterCommand = new RelayCommand(_ => RemoveMonster());
        MoveMonsterUpCommand = new RelayCommand(_ => MoveMonster(-1));
        MoveMonsterDownCommand = new RelayCommand(_ => MoveMonster(1));
        ClearAlertHistoryCommand = new RelayCommand(_ => AutomationEventHub.Shared.Clear());
        foreach (var item in AutomationEventHub.Shared.Snapshot().Reverse()) AlertHistory.Add(item);
        AutomationEventHub.Shared.Published += OnAutomationEvent;
        AutomationEventHub.Shared.Cleared += OnAutomationEventsCleared;
        try { Apply(BotProfileService.Load()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Apply(BotProfileService.Normalize(new BotProfile()));
            Feedback = $"Perfil local não pôde ser lido: {ex.Message}";
        }
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "Perfil JSON (*.json)|*.json", Title = "Importar perfil JSON" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Apply(BotProfileService.Import(File.ReadAllText(dialog.FileName)));
            Feedback = "Perfil importado. Revise as opções e clique em Salvar perfil para mantê-las no KBot.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Feedback = $"Não foi possível importar: {ex.Message}";
        }
    }

    private void Save()
    {
        var profile = TryBuildProfile();
        if (profile is null) return;

        try
        {
            BotProfileService.Save(profile);
            Feedback = $"Perfil salvo em {BotProfileService.ProfilePath}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Feedback = $"Não foi possível salvar: {ex.Message}";
        }
    }

    private BotProfile? TryBuildProfile()
    {
        if (!int.TryParse(ReviveHp, out var reviveHp) || reviveHp < 0 ||
            !int.TryParse(ReviveOutOfBattleHp, out var outOfBattleHp) || outOfBattleHp < 0)
        {
            Feedback = "Os limites de HP devem ser números inteiros maiores ou iguais a zero.";
            return null;
        }
        if (!int.TryParse(FishingX, out var fishingX) || fishingX < 0 ||
            !int.TryParse(FishingY, out var fishingY) || fishingY < 0)
        {
            Feedback = "As coordenadas da pesca devem ser números inteiros maiores ou iguais a zero.";
            return null;
        }
        if (!int.TryParse(FishingDelaySeconds, out var fishingDelaySeconds) || fishingDelaySeconds < 1)
        {
            Feedback = "O delay da pesca deve ser um número inteiro de segundos maior ou igual a 1.";
            return null;
        }
        if (!int.TryParse(FishingMaxPoke, out var fishingMaxPoke) || fishingMaxPoke < -1)
        {
            Feedback = "O limite de pokes perto deve ser um número inteiro (use -1 para nunca pausar).";
            return null;
        }
        if (!int.TryParse(FishingRaio, out var fishingRaio) || fishingRaio < 1 || fishingRaio > 12)
        {
            Feedback = "O alcance da busca do ponto de pesca deve estar entre 1 e 12.";
            return null;
        }
        if (!int.TryParse(FishingWaterId, out var fishingWaterId) || fishingWaterId <= 0)
        {
            Feedback = "O ID do ponto de pesca deve ser um número inteiro maior que zero.";
            return null;
        }
        if (!int.TryParse(AntiAfkIdleSeconds, out var antiAfkIdleSeconds) || antiAfkIdleSeconds < 15)
        {
            Feedback = "O tempo de inatividade do anti-AFK deve ser um número inteiro de segundos maior ou igual a 15.";
            return null;
        }
        if (!int.TryParse(VigiaDistThreshold, out var vigiaDistThreshold) || vigiaDistThreshold < 2)
        {
            Feedback = "O salto que conta como puxão deve ser um número inteiro de tiles maior ou igual a 2.";
            return null;
        }
        if (!int.TryParse(EndgameWaveCount, out var egWaveCount) || egWaveCount < 1 ||
            !int.TryParse(EndgameRingTiles, out var egRingTiles) || egRingTiles < 1 ||
            !int.TryParse(EndgameSeeStop, out var egSeeStop) || egSeeStop < 1 ||
            !int.TryParse(EndgameStopDist, out var egStopDist) || egStopDist < 1 ||
            !int.TryParse(EndgameApproachSqm, out var egApproachSqm) || egApproachSqm < 1)
        {
            Feedback = "O auto combo (end game): wave, anel, parada e aproximação devem ser números inteiros maiores ou iguais a 1.";
            return null;
        }
        if (!int.TryParse(EndgameRelureS, out var egRelureS) || egRelureS < 0 ||
            !int.TryParse(EndgameMoveGapMs, out var egMoveGapMs) || egMoveGapMs < 60)
        {
            Feedback = "O auto combo (end game): relure em segundos (0 = espera para sempre) e gap de moves em ms (mínimo 60) devem ser inteiros válidos.";
            return null;
        }
        if (!int.TryParse(EndgamePotItem, out var egPotItem) || egPotItem < 0 ||
            (egPotItem > 0 && egPotItem < 100))
        {
            Feedback = "O item de potion do auto combo deve ser 0 (sem potion) ou o ID do servidor (100 ou mais).";
            return null;
        }
        if (!int.TryParse(EndgamePotPct, out var egPotPct) || egPotPct < 1 || egPotPct > 100 ||
            !int.TryParse(EndgameSavePct, out var egSavePct) || egSavePct < 0 || egSavePct > 100 ||
            !int.TryParse(EndgameSwapPct, out var egSwapPct) || egSwapPct < 0 || egSwapPct > 100)
        {
            Feedback = "Os percentuais do auto combo devem estar entre 0 e 100 (potion entre 1 e 100).";
            return null;
        }
        if (!int.TryParse(EndgameSafeReach, out var egSafeReach) || egSafeReach < 0 ||
            !int.TryParse(EndgameRecoverMaxS, out var egRecoverMaxS) || egRecoverMaxS < 0 ||
            !int.TryParse(EndgameReburst, out var egReburst) || egReburst < 0)
        {
            Feedback = "O auto combo: alcance do safe spot, tempo máximo de cooldown e re-combo devem ser inteiros maiores ou iguais a zero.";
            return null;
        }
        if (!int.TryParse(EndgameSafeX, out var egSafeX) || egSafeX < 0 ||
            !int.TryParse(EndgameSafeY, out var egSafeY) || egSafeY < 0)
        {
            Feedback = "As coordenadas do safe spot do auto combo devem ser números inteiros maiores ou iguais a zero.";
            return null;
        }
        if (CureAtPercent < 0 || CureAtPercent > 100)
        {
            Feedback = "O limite de cura deve estar entre 0 e 100.";
            return null;
        }
        if (ActiveSlot < 0 || ActiveSlot > 6)
        {
            Feedback = "O slot de pokemon deve estar entre 0 e 6.";
            return null;
        }
        if (!int.TryParse(AttackRange, out var attackRange) || attackRange < 1 || attackRange > 20)
        {
            Feedback = "O alcance do alvo deve estar entre 1 e 20 SQMs.";
            return null;
        }
        if (!int.TryParse(TargetMoveIntervalMs, out var targetMoveIntervalMs) || targetMoveIntervalMs < 100 ||
            !int.TryParse(TargetKeepDistance, out var targetKeepDistance) || targetKeepDistance < 0 || targetKeepDistance > 20)
        {
            Feedback = "O movimento do alvo exige intervalo de pelo menos 100 ms e distância entre 0 e 20 SQMs.";
            return null;
        }
        if (!int.TryParse(PlayerHealPercent, out var playerHealPercent) || playerHealPercent < 1 || playerHealPercent > 100 ||
            !int.TryParse(HealingCooldownMs, out var healingCooldownMs) || healingCooldownMs < 250)
        {
            Feedback = "A cura do personagem exige HP entre 1 e 100 e cooldown de pelo menos 250 ms.";
            return null;
        }
        if (!int.TryParse(CatchDelayMs, out var catchDelayMs) || catchDelayMs < 0 || catchDelayMs > 60000)
        {
            Feedback = "O atraso da captura deve estar entre 0 e 60000 ms.";
            return null;
        }
        if (!int.TryParse(ShinyBallId, out var shinyBallId) || (shinyBallId != 0 && shinyBallId < 100))
        {
            Feedback = "A bola de shiny deve ser 0 (desligada) ou um ID igual ou maior que 100.";
            return null;
        }
        if (!TryParseCatchRules(CatchRules, out var catchEntries, out var catchRuleError))
        {
            Feedback = catchRuleError;
            return null;
        }
        var spells = new System.Collections.Generic.List<SpellSetting>();
        foreach (var editor in Spells)
        {
            if (!int.TryParse(editor.CooldownText, out var cooldown) || cooldown < 0)
            {
                Feedback = $"Cooldown inválido para {editor.Key}. Use segundos inteiros maiores ou iguais a zero.";
                return null;
            }
            spells.Add(new SpellSetting { Key = editor.Key, Enabled = editor.Enabled, CooldownSeconds = cooldown });
        }

        return new BotProfile
        {
            MonstersToAttack = Monsters.ToList(),
            AttackerEnabled = AttackerEnabled,
            AreaCombo = AreaCombo,
            AttackOneByOne = AttackOneByOne,
            AutoSummon = AutoSummon,
            ActiveSlot = ActiveSlot,
            AttackRange = attackRange,
            IgnoredMonsters = SplitList(IgnoredMonsters),
            RareWords = SplitList(RareWords),
            RareFirst = RareFirst,
            TargetMoveEnabled = TargetMoveEnabled,
            TargetApproachEnabled = TargetApproachEnabled,
            TargetFollowInBattle = TargetFollowInBattle,
            TargetMoveIntervalMs = targetMoveIntervalMs,
            TargetKeepDistance = targetKeepDistance,
            PauseRouteOnTarget = PauseRouteOnTarget,
            AutoReviveEnabled = AutoReviveEnabled,
            FoodEnabled = FoodEnabled,
            AutoPotion = AutoPotion,
            AutoMedicine = AutoMedicine,
            HealPlayer = HealPlayer,
            CureAtPercent = CureAtPercent,
            PlayerHealPercent = playerHealPercent,
            HealingCooldownMs = healingCooldownMs,
            HealOnlyOutOfBattle = HealOnlyOutOfBattle,
            ReviveHp = reviveHp,
            ReviveOutOfBattleHp = outOfBattleHp,
            ReviveItemHotkey = ReviveItemHotkey,
            FoodHotkey = FoodHotkey,
            MedicineHotkey = MedicineHotkey,
            HealHotkey = HealHotkey,
            AlertsEnabled = AlertsEnabled,
            HotkeysEnabled = HotkeysEnabled,
            ManualReviveHotkey = ManualReviveHotkey,
            PauseCavebotHotkey = PauseCavebotHotkey,
            PauseAttackerHotkey = PauseAttackerHotkey,
            FishingEnabled = FishingEnabled,
            FishingHotkey = FishingHotkey,
            FishingX = fishingX,
            FishingY = fishingY,
            FishingDelaySeconds = fishingDelaySeconds,
            FishingMaxPoke = fishingMaxPoke,
            FishingRaio = fishingRaio,
            FishingWaterId = fishingWaterId,
            CatchEnabled = CatchEnabled,
            CatchHotkey = CatchHotkey,
            CatchDelayMs = catchDelayMs,
            CatchShinyEnabled = CatchShinyEnabled,
            ShinyBallId = shinyBallId,
            CatchEntries = catchEntries,
            LootEnabled = LootEnabled,
            LootHotkey = LootHotkey,
            AntiAfkEnabled = AntiAfkEnabled,
            AntiAfkIdleSeconds = antiAfkIdleSeconds,
            VigiaEnabled = VigiaEnabled,
            VigiaDistThreshold = vigiaDistThreshold,
            EndgameEnabled = EndgameEnabled,
            EndgameT1Tank = EndgameT1Tank.Trim(),
            EndgameT1D1 = EndgameT1D1.Trim(),
            EndgameT1D2 = EndgameT1D2.Trim(),
            EndgameT2Tank = EndgameT2Tank.Trim(),
            EndgameT2D1 = EndgameT2D1.Trim(),
            EndgameT2D2 = EndgameT2D2.Trim(),
            EndgameWaveCount = egWaveCount,
            EndgameRingTiles = egRingTiles,
            EndgameSeeStop = egSeeStop,
            EndgameStopDist = egStopDist,
            EndgameApproachSqm = egApproachSqm,
            EndgameRelureS = egRelureS,
            EndgameMoveGapMs = egMoveGapMs,
            EndgamePotItem = egPotItem,
            EndgamePotPct = egPotPct,
            EndgameSavePct = egSavePct,
            EndgameSwapPct = egSwapPct,
            EndgameUseSafe = EndgameUseSafe,
            EndgameSafeReach = egSafeReach,
            EndgameRecoverMaxS = egRecoverMaxS,
            EndgameReburst = egReburst,
            EndgamePokeStop = EndgamePokeStop,
            EndgamePokeStopCmd = string.IsNullOrWhiteSpace(EndgamePokeStopCmd) ? "!pokestop" : EndgamePokeStopCmd.Trim(),
            EndgameSafeX = egSafeX,
            EndgameSafeY = egSafeY,
            PmReplyEnabled = PmReplyEnabled,
            PmPhrases = PmPhrases,
            Spells = spells
        };
    }

    private void ExportCode()
    {
        var profile = TryBuildProfile();
        if (profile is null) return;
        var code = ConfigShareService.Export(profile);
        try
        {
            Clipboard.SetText(code);
            Feedback = $"Código copiado ({code.Length} caracteres). É só colar para o seu amigo.";
        }
        catch
        {
            Feedback = code;
        }
    }

    private void ImportCode()
    {
        var code = PromptInput("Cole aqui o código do seu amigo", "Compartilhar configuração");
        if (string.IsNullOrWhiteSpace(code)) return;
        var baseProfile = TryBuildProfile();
        if (baseProfile is null) return;
        var result = ConfigShareService.Import(code, baseProfile);
        if (result.Message.StartsWith("Código") || result.Message.StartsWith("Isso"))
        {
            Feedback = result.Message;
            return;
        }
        try
        {
            BotProfileService.Save(baseProfile);
            Apply(baseProfile);
            Feedback = $"{result.Message} Salvo no KBot.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Feedback = $"Código lido, mas o perfil não pôde ser salvo: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task DiagnoseAsync()
    {
        NativeStatus? status = null;
        try { status = await _nativeService.GetStatusAsync(CancellationToken.None); }
        catch { /* diagnóstico segue sem estado do núcleo */ }

        Diagnostics.Clear();
        foreach (var item in ConfigDiagnosticsService.Diagnose(BuildDiagnosedProfile(), status))
            Diagnostics.Add(item);
    }

    private BotProfile BuildDiagnosedProfile()
    {
        return TryBuildProfile() ?? new BotProfile();
    }

    private static string PromptInput(string prompt, string title)
    {
        var dialog = new System.Windows.Window
        {
            Title = title,
            Width = 560,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.Black
        };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var box = new System.Windows.Controls.TextBox { Height = 44, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(box);
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        string? result = null;
        var ok = new System.Windows.Controls.Button { Content = "Aplicar", MinWidth = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { result = box.Text; dialog.Close(); };
        var cancel = new System.Windows.Controls.Button { Content = "Cancelar", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => dialog.Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();
        return result ?? string.Empty;
    }

    private void Apply(BotProfile profile)
    {
        Monsters.Clear();
        foreach (var monster in profile.MonstersToAttack) Monsters.Add(monster);
        Spells.Clear();
        foreach (var spell in profile.Spells)
            Spells.Add(new SpellEditor { Key = spell.Key, Enabled = spell.Enabled, CooldownText = spell.CooldownSeconds.ToString() });
        SelectedMonster = null;
        AttackerEnabled = profile.AttackerEnabled;
        AreaCombo = profile.AreaCombo;
        AttackOneByOne = profile.AttackOneByOne;
        AutoSummon = profile.AutoSummon;
        ActiveSlot = profile.ActiveSlot;
        AttackRange = profile.AttackRange.ToString();
        IgnoredMonsters = string.Join(", ", profile.IgnoredMonsters);
        RareWords = string.Join(", ", profile.RareWords);
        RareFirst = profile.RareFirst;
        TargetMoveEnabled = profile.TargetMoveEnabled;
        TargetApproachEnabled = profile.TargetApproachEnabled;
        TargetFollowInBattle = profile.TargetFollowInBattle;
        TargetMoveIntervalMs = profile.TargetMoveIntervalMs.ToString();
        TargetKeepDistance = profile.TargetKeepDistance.ToString();
        PauseRouteOnTarget = profile.PauseRouteOnTarget;
        AutoReviveEnabled = profile.AutoReviveEnabled;
        FoodEnabled = profile.FoodEnabled;
        AutoPotion = profile.AutoPotion;
        AutoMedicine = profile.AutoMedicine;
        HealPlayer = profile.HealPlayer;
        CureAtPercent = profile.CureAtPercent;
        PlayerHealPercent = profile.PlayerHealPercent.ToString();
        HealingCooldownMs = profile.HealingCooldownMs.ToString();
        HealOnlyOutOfBattle = profile.HealOnlyOutOfBattle;
        ReviveHp = profile.ReviveHp.ToString();
        ReviveOutOfBattleHp = profile.ReviveOutOfBattleHp.ToString();
        ReviveItemHotkey = profile.ReviveItemHotkey;
        FoodHotkey = profile.FoodHotkey;
        MedicineHotkey = profile.MedicineHotkey;
        HealHotkey = profile.HealHotkey;
        AlertsEnabled = profile.AlertsEnabled;
        HotkeysEnabled = profile.HotkeysEnabled;
        ManualReviveHotkey = profile.ManualReviveHotkey;
        PauseCavebotHotkey = profile.PauseCavebotHotkey;
        PauseAttackerHotkey = profile.PauseAttackerHotkey;
        FishingEnabled = profile.FishingEnabled;
        FishingHotkey = profile.FishingHotkey;
        FishingX = profile.FishingX.ToString();
        FishingY = profile.FishingY.ToString();
        FishingDelaySeconds = profile.FishingDelaySeconds.ToString();
        FishingMaxPoke = profile.FishingMaxPoke.ToString();
        FishingRaio = profile.FishingRaio.ToString();
        FishingWaterId = profile.FishingWaterId.ToString();
        CatchEnabled = profile.CatchEnabled;
        CatchHotkey = profile.CatchHotkey;
        CatchDelayMs = profile.CatchDelayMs.ToString();
        CatchShinyEnabled = profile.CatchShinyEnabled;
        ShinyBallId = profile.ShinyBallId.ToString();
        CatchRules = string.Join(Environment.NewLine, profile.CatchEntries.Select(entry => $"{entry.Name}:{entry.CorpseId}:{entry.BallId}"));
        LootEnabled = profile.LootEnabled;
        LootHotkey = profile.LootHotkey;
        AntiAfkEnabled = profile.AntiAfkEnabled;
        AntiAfkIdleSeconds = profile.AntiAfkIdleSeconds.ToString();
        VigiaEnabled = profile.VigiaEnabled;
        VigiaDistThreshold = profile.VigiaDistThreshold.ToString();
        EndgameEnabled = profile.EndgameEnabled;
        EndgameT1Tank = profile.EndgameT1Tank;
        EndgameT1D1 = profile.EndgameT1D1;
        EndgameT1D2 = profile.EndgameT1D2;
        EndgameT2Tank = profile.EndgameT2Tank;
        EndgameT2D1 = profile.EndgameT2D1;
        EndgameT2D2 = profile.EndgameT2D2;
        EndgameWaveCount = profile.EndgameWaveCount.ToString();
        EndgameRingTiles = profile.EndgameRingTiles.ToString();
        EndgameSeeStop = profile.EndgameSeeStop.ToString();
        EndgameStopDist = profile.EndgameStopDist.ToString();
        EndgameApproachSqm = profile.EndgameApproachSqm.ToString();
        EndgameRelureS = profile.EndgameRelureS.ToString();
        EndgameMoveGapMs = profile.EndgameMoveGapMs.ToString();
        EndgamePotItem = profile.EndgamePotItem.ToString();
        EndgamePotPct = profile.EndgamePotPct.ToString();
        EndgameSavePct = profile.EndgameSavePct.ToString();
        EndgameSwapPct = profile.EndgameSwapPct.ToString();
        EndgameUseSafe = profile.EndgameUseSafe;
        EndgameSafeReach = profile.EndgameSafeReach.ToString();
        EndgameRecoverMaxS = profile.EndgameRecoverMaxS.ToString();
        EndgameReburst = profile.EndgameReburst.ToString();
        EndgamePokeStop = profile.EndgamePokeStop;
        EndgamePokeStopCmd = profile.EndgamePokeStopCmd;
        EndgameSafeX = profile.EndgameSafeX.ToString();
        EndgameSafeY = profile.EndgameSafeY.ToString();
        PmReplyEnabled = profile.PmReplyEnabled;
        PmPhrases = profile.PmPhrases;
    }

    private void AddMonster()
    {
        var name = NewMonster.Trim();
        if (name.Length == 0) { Feedback = "Digite o nome de uma criatura."; return; }
        if (Monsters.Any(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase)))
        {
            Feedback = "Essa criatura já está na lista.";
            return;
        }
        Monsters.Add(name);
        SelectedMonster = name;
        NewMonster = string.Empty;
        Feedback = "Criatura adicionada. A ordem da lista define a prioridade futura.";
    }

    private static bool TryParseCatchRules(string value,
        out System.Collections.Generic.List<KBot.App.BotBrain.CatchEntry> entries, out string error)
    {
        entries = new();
        error = string.Empty;
        var lines = value.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var parts = line.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || parts[0].Length == 0 || !int.TryParse(parts[1], out var corpseId) || corpseId <= 0 ||
                !int.TryParse(parts[2], out var ballId) || ballId < 100)
            {
                error = $"Regra de captura inválida: '{line}'. Use Nome:CorpseId:BallId, com IDs positivos e bola >= 100.";
                entries.Clear();
                return false;
            }
            entries.Add(new KBot.App.BotBrain.CatchEntry(parts[0], corpseId, ballId));
        }
        return true;
    }

    private static System.Collections.Generic.List<string> SplitList(string value) =>
        value.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void OnAutomationEvent(AutomationEvent item)
    {
        void Add()
        {
            AlertHistory.Insert(0, item);
            while (AlertHistory.Count > 250) AlertHistory.RemoveAt(AlertHistory.Count - 1);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Add();
        else dispatcher.BeginInvoke(Add);
    }

    private void OnAutomationEventsCleared()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) AlertHistory.Clear();
        else dispatcher.BeginInvoke(AlertHistory.Clear);
    }

    public void Dispose()
    {
        AutomationEventHub.Shared.Published -= OnAutomationEvent;
        AutomationEventHub.Shared.Cleared -= OnAutomationEventsCleared;
        _nativeService.Dispose();
    }

    private void RemoveMonster()
    {
        if (SelectedMonster is null) return;
        Monsters.Remove(SelectedMonster);
        SelectedMonster = null;
        Feedback = "Criatura removida. Salve o perfil para guardar a alteração.";
    }

    private void MoveMonster(int direction)
    {
        if (SelectedMonster is null) return;
        int index = Monsters.IndexOf(SelectedMonster);
        int destination = index + direction;
        if (destination < 0 || destination >= Monsters.Count) return;
        Monsters.Move(index, destination);
        Feedback = "Prioridade alterada. Salve o perfil para guardar a alteração.";
    }
}
