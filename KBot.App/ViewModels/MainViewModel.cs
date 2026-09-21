using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using KBot.App.Views;
using KBot.App.Services;

namespace KBot.App.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private object _currentView;
        private string _currentSection = "Dashboard";
        private readonly Dictionary<string, UserControl> _moduleViews;
        private readonly CavebotViewModel _cavebotViewModel;
        private readonly SettingsViewModel _settingsViewModel;

        public MainViewModel(KBotLifecycle lifecycle)
        {
            DashboardView = new DashboardView(lifecycle);
            CavebotView = new CavebotView();
            _cavebotViewModel = CavebotView.DataContext as CavebotViewModel ?? new CavebotViewModel();
            if (!ReferenceEquals(CavebotView.DataContext, _cavebotViewModel)) CavebotView.DataContext = _cavebotViewModel;
            _settingsViewModel = new SettingsViewModel();
            SettingsView = new SettingsView(_settingsViewModel, "Settings");
            _moduleViews = new Dictionary<string, UserControl>
            {
                ["Target"] = new TargetView(_settingsViewModel),
                ["Healing"] = new HealingView(_settingsViewModel),
                ["Catch"] = new CatchView(_settingsViewModel),
                ["Loot"] = new LootView(_settingsViewModel),
                ["Fishing"] = new FishingView(_settingsViewModel),
                ["Alerts"] = new AlertsView(_settingsViewModel),
                ["Settings"] = SettingsView
            };
            _currentView = DashboardView;
            NavigateCommand = new RelayCommand(parameter => Navigate(parameter as string));
        }

        public DashboardView DashboardView { get; }
        public CavebotView CavebotView { get; }
        public SettingsView SettingsView { get; }
        public object CurrentView { get => _currentView; private set => Set(ref _currentView, value); }
        public string CurrentSection { get => _currentSection; private set => Set(ref _currentSection, value); }
        public string CurrentSectionTitle => CurrentSection switch
        {
            "Dashboard" => "Início",
            "Target" => "Alvo",
            "Healing" => "Cura",
            "Catch" => "Captura",
            "Loot" => "Coleta",
            "Fishing" => "Pesca",
            "CaveBot" => "Rota",
            "Alerts" => "Alertas",
            "Settings" => "Configurações",
            _ => CurrentSection
        };
        public string CurrentSectionDescription => CurrentSection switch
        {
            "Dashboard" => "Acompanhe o núcleo, o cliente e o estado da leitura em um só lugar.",
            "Target" => "Defina quais criaturas entram na seleção e em que ordem.",
            "Healing" => "Ajuste revive, comida e o intervalo das magias.",
            "Catch" => "Guarde as preferências de captura no perfil.",
            "Loot" => "Guarde as preferências de coleta no perfil.",
            "Fishing" => "Defina o ponto e o atalho usados na pesca.",
            "CaveBot" => "Prepare, importe e organize suas rotas.",
            "Alerts" => "Ajuste alertas e atalhos de controle.",
            "Settings" => "Organize os recursos do KBot em um perfil local.",
            _ => "Organize as configurações de automação do KBot."
        };
        public ICommand NavigateCommand { get; }

        public bool IsDashboardSelected => CurrentSection == "Dashboard";
        public bool IsTargetSelected => CurrentSection == "Target";
        public bool IsHealingSelected => CurrentSection == "Healing";
        public bool IsCatchSelected => CurrentSection == "Catch";
        public bool IsLootSelected => CurrentSection == "Loot";
        public bool IsFishingSelected => CurrentSection == "Fishing";
        public bool IsCaveBotSelected => CurrentSection == "CaveBot";
        public bool IsAlertsSelected => CurrentSection == "Alerts";
        public bool IsSettingsSelected => CurrentSection == "Settings";

        public async Task DisposeAsync()
        {
            await DashboardView.DisposeAsync();
            _cavebotViewModel.Dispose();
            _settingsViewModel.Dispose();
        }

        private void Navigate(string? section)
        {
            if (string.IsNullOrWhiteSpace(section) || section == CurrentSection) return;

            CurrentSection = section;
            if (section == "Dashboard") CurrentView = DashboardView;
            else if (section == "CaveBot") CurrentView = CavebotView;
            else if (_moduleViews.TryGetValue(section, out var view)) CurrentView = view;
            else return;

            OnPropertyChanged(nameof(CurrentSectionTitle));
            OnPropertyChanged(nameof(CurrentSectionDescription));

            OnPropertyChanged(nameof(IsDashboardSelected));
            OnPropertyChanged(nameof(IsTargetSelected));
            OnPropertyChanged(nameof(IsHealingSelected));
            OnPropertyChanged(nameof(IsCatchSelected));
            OnPropertyChanged(nameof(IsLootSelected));
            OnPropertyChanged(nameof(IsFishingSelected));
            OnPropertyChanged(nameof(IsCaveBotSelected));
            OnPropertyChanged(nameof(IsAlertsSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
        }

    }
}
