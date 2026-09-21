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

public sealed class SettingsViewModel : ObservableObject
{
    private string _newMonster = string.Empty;
    private string? _selectedMonster;
    private bool _attackerEnabled;
    private bool _areaCombo = true;
    private bool _attackOneByOne;
    private bool _autoSummon = true;
    private int _activeSlot;
    private bool _autoReviveEnabled;
    private bool _foodEnabled;
    private bool _autoPotion;
    private bool _autoMedicine = true;
    private bool _healPlayer = true;
    private int _cureAtPercent = 70;
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
    private bool _catchEnabled;
    private string _catchHotkey = string.Empty;
    private bool _lootEnabled;
    private string _lootHotkey = string.Empty;
    private bool _pmReplyEnabled;
    private string _pmPhrases = string.Empty;
    private readonly NativeService _nativeService = new();
    private string _feedback = "Configurações locais. Nenhuma automação será iniciada nesta tela.";

    public ObservableCollection<string> Monsters { get; } = new();
    public ObservableCollection<SpellEditor> Spells { get; } = new();
    public ObservableCollection<KBot.App.Services.DiagnosticItem> Diagnostics { get; } = new();
    public string NewMonster { get => _newMonster; set => Set(ref _newMonster, value); }
    public string? SelectedMonster { get => _selectedMonster; set { Set(ref _selectedMonster, value); OnPropertyChanged(nameof(HasSelectedMonster)); } }
    public bool HasSelectedMonster => SelectedMonster is not null;
    public bool AttackerEnabled { get => _attackerEnabled; set => Set(ref _attackerEnabled, value); }
    public bool AreaCombo { get => _areaCombo; set => Set(ref _areaCombo, value); }
    public bool AttackOneByOne { get => _attackOneByOne; set => Set(ref _attackOneByOne, value); }
    public bool AutoSummon { get => _autoSummon; set => Set(ref _autoSummon, value); }
    public int ActiveSlot { get => _activeSlot; set => Set(ref _activeSlot, value); }
    public bool AutoReviveEnabled { get => _autoReviveEnabled; set => Set(ref _autoReviveEnabled, value); }
    public bool FoodEnabled { get => _foodEnabled; set => Set(ref _foodEnabled, value); }
    public bool AutoPotion { get => _autoPotion; set => Set(ref _autoPotion, value); }
    public bool AutoMedicine { get => _autoMedicine; set => Set(ref _autoMedicine, value); }
    public bool HealPlayer { get => _healPlayer; set => Set(ref _healPlayer, value); }
    public int CureAtPercent { get => _cureAtPercent; set => Set(ref _cureAtPercent, value); }
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
    public bool CatchEnabled { get => _catchEnabled; set => Set(ref _catchEnabled, value); }
    public string CatchHotkey { get => _catchHotkey; set => Set(ref _catchHotkey, value); }
    public bool LootEnabled { get => _lootEnabled; set => Set(ref _lootEnabled, value); }
    public string LootHotkey { get => _lootHotkey; set => Set(ref _lootHotkey, value); }
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
            AutoReviveEnabled = AutoReviveEnabled,
            FoodEnabled = FoodEnabled,
            AutoPotion = AutoPotion,
            AutoMedicine = AutoMedicine,
            HealPlayer = HealPlayer,
            CureAtPercent = CureAtPercent,
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
            CatchEnabled = CatchEnabled,
            CatchHotkey = CatchHotkey,
            LootEnabled = LootEnabled,
            LootHotkey = LootHotkey,
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
        AutoReviveEnabled = profile.AutoReviveEnabled;
        FoodEnabled = profile.FoodEnabled;
        AutoPotion = profile.AutoPotion;
        AutoMedicine = profile.AutoMedicine;
        HealPlayer = profile.HealPlayer;
        CureAtPercent = profile.CureAtPercent;
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
        CatchEnabled = profile.CatchEnabled;
        CatchHotkey = profile.CatchHotkey;
        LootEnabled = profile.LootEnabled;
        LootHotkey = profile.LootHotkey;
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
