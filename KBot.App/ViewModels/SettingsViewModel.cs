using KBot.App.Models;
using KBot.App.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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

public sealed class SettingsViewModel : ObservableObject
{
    private string _newMonster = string.Empty;
    private string? _selectedMonster;
    private bool _attackerEnabled;
    private bool _autoReviveEnabled;
    private bool _foodEnabled;
    private string _reviveHp = "0";
    private string _reviveOutOfBattleHp = "0";
    private string _reviveItemHotkey = "F9";
    private string _foodHotkey = "F10";
    private bool _alertsEnabled;
    private bool _hotkeysEnabled;
    private string _manualReviveHotkey = string.Empty;
    private string _pauseCavebotHotkey = string.Empty;
    private string _pauseAttackerHotkey = string.Empty;
    private bool _fishingEnabled;
    private string _fishingHotkey = "Ctrl+Z";
    private string _fishingX = "0";
    private string _fishingY = "0";
    private bool _catchEnabled;
    private string _catchHotkey = string.Empty;
    private bool _lootEnabled;
    private string _lootHotkey = string.Empty;
    private string _feedback = "Configurações locais. Nenhuma automação será iniciada nesta tela.";

    public ObservableCollection<string> Monsters { get; } = new();
    public ObservableCollection<SpellEditor> Spells { get; } = new();
    public string NewMonster { get => _newMonster; set => Set(ref _newMonster, value); }
    public string? SelectedMonster { get => _selectedMonster; set { Set(ref _selectedMonster, value); OnPropertyChanged(nameof(HasSelectedMonster)); } }
    public bool HasSelectedMonster => SelectedMonster is not null;
    public bool AttackerEnabled { get => _attackerEnabled; set => Set(ref _attackerEnabled, value); }
    public bool AutoReviveEnabled { get => _autoReviveEnabled; set => Set(ref _autoReviveEnabled, value); }
    public bool FoodEnabled { get => _foodEnabled; set => Set(ref _foodEnabled, value); }
    public string ReviveHp { get => _reviveHp; set => Set(ref _reviveHp, value); }
    public string ReviveOutOfBattleHp { get => _reviveOutOfBattleHp; set => Set(ref _reviveOutOfBattleHp, value); }
    public string ReviveItemHotkey { get => _reviveItemHotkey; set => Set(ref _reviveItemHotkey, value); }
    public string FoodHotkey { get => _foodHotkey; set => Set(ref _foodHotkey, value); }
    public bool AlertsEnabled { get => _alertsEnabled; set => Set(ref _alertsEnabled, value); }
    public bool HotkeysEnabled { get => _hotkeysEnabled; set => Set(ref _hotkeysEnabled, value); }
    public string ManualReviveHotkey { get => _manualReviveHotkey; set => Set(ref _manualReviveHotkey, value); }
    public string PauseCavebotHotkey { get => _pauseCavebotHotkey; set => Set(ref _pauseCavebotHotkey, value); }
    public string PauseAttackerHotkey { get => _pauseAttackerHotkey; set => Set(ref _pauseAttackerHotkey, value); }
    public bool FishingEnabled { get => _fishingEnabled; set => Set(ref _fishingEnabled, value); }
    public string FishingHotkey { get => _fishingHotkey; set => Set(ref _fishingHotkey, value); }
    public string FishingX { get => _fishingX; set => Set(ref _fishingX, value); }
    public string FishingY { get => _fishingY; set => Set(ref _fishingY, value); }
    public bool CatchEnabled { get => _catchEnabled; set => Set(ref _catchEnabled, value); }
    public string CatchHotkey { get => _catchHotkey; set => Set(ref _catchHotkey, value); }
    public bool LootEnabled { get => _lootEnabled; set => Set(ref _lootEnabled, value); }
    public string LootHotkey { get => _lootHotkey; set => Set(ref _lootHotkey, value); }
    public string Feedback { get => _feedback; private set => Set(ref _feedback, value); }

    public ICommand ImportCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddMonsterCommand { get; }
    public ICommand RemoveMonsterCommand { get; }
    public ICommand MoveMonsterUpCommand { get; }
    public ICommand MoveMonsterDownCommand { get; }

    public SettingsViewModel()
    {
        ImportCommand = new RelayCommand(_ => Import());
        SaveCommand = new RelayCommand(_ => Save());
        AddMonsterCommand = new RelayCommand(_ => AddMonster());
        RemoveMonsterCommand = new RelayCommand(_ => RemoveMonster());
        MoveMonsterUpCommand = new RelayCommand(_ => MoveMonster(-1));
        MoveMonsterDownCommand = new RelayCommand(_ => MoveMonster(1));
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
        if (!int.TryParse(ReviveHp, out var reviveHp) || reviveHp < 0 ||
            !int.TryParse(ReviveOutOfBattleHp, out var outOfBattleHp) || outOfBattleHp < 0)
        {
            Feedback = "Os limites de HP devem ser números inteiros maiores ou iguais a zero.";
            return;
        }
        if (!int.TryParse(FishingX, out var fishingX) || fishingX < 0 ||
            !int.TryParse(FishingY, out var fishingY) || fishingY < 0)
        {
            Feedback = "As coordenadas da pesca devem ser números inteiros maiores ou iguais a zero.";
            return;
        }
        var spells = new System.Collections.Generic.List<SpellSetting>();
        foreach (var editor in Spells)
        {
            if (!int.TryParse(editor.CooldownText, out var cooldown) || cooldown < 0)
            {
                Feedback = $"Cooldown inválido para {editor.Key}. Use segundos inteiros maiores ou iguais a zero.";
                return;
            }
            spells.Add(new SpellSetting { Key = editor.Key, Enabled = editor.Enabled, CooldownSeconds = cooldown });
        }

        try
        {
            BotProfileService.Save(new BotProfile
            {
                MonstersToAttack = Monsters.ToList(),
                AttackerEnabled = AttackerEnabled,
                AutoReviveEnabled = AutoReviveEnabled,
                FoodEnabled = FoodEnabled,
                ReviveHp = reviveHp,
                ReviveOutOfBattleHp = outOfBattleHp,
                ReviveItemHotkey = ReviveItemHotkey,
                FoodHotkey = FoodHotkey,
                AlertsEnabled = AlertsEnabled,
                HotkeysEnabled = HotkeysEnabled,
                ManualReviveHotkey = ManualReviveHotkey,
                PauseCavebotHotkey = PauseCavebotHotkey,
                PauseAttackerHotkey = PauseAttackerHotkey,
                FishingEnabled = FishingEnabled,
                FishingHotkey = FishingHotkey,
                FishingX = fishingX,
                FishingY = fishingY,
                CatchEnabled = CatchEnabled,
                CatchHotkey = CatchHotkey,
                LootEnabled = LootEnabled,
                LootHotkey = LootHotkey,
                Spells = spells
            });
            Feedback = $"Perfil salvo em {BotProfileService.ProfilePath}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Feedback = $"Não foi possível salvar: {ex.Message}";
        }
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
        AutoReviveEnabled = profile.AutoReviveEnabled;
        FoodEnabled = profile.FoodEnabled;
        ReviveHp = profile.ReviveHp.ToString();
        ReviveOutOfBattleHp = profile.ReviveOutOfBattleHp.ToString();
        ReviveItemHotkey = profile.ReviveItemHotkey;
        FoodHotkey = profile.FoodHotkey;
        AlertsEnabled = profile.AlertsEnabled;
        HotkeysEnabled = profile.HotkeysEnabled;
        ManualReviveHotkey = profile.ManualReviveHotkey;
        PauseCavebotHotkey = profile.PauseCavebotHotkey;
        PauseAttackerHotkey = profile.PauseAttackerHotkey;
        FishingEnabled = profile.FishingEnabled;
        FishingHotkey = profile.FishingHotkey;
        FishingX = profile.FishingX.ToString();
        FishingY = profile.FishingY.ToString();
        CatchEnabled = profile.CatchEnabled;
        CatchHotkey = profile.CatchHotkey;
        LootEnabled = profile.LootEnabled;
        LootHotkey = profile.LootHotkey;
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
