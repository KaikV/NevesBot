using KBot.App.Models;
using KBot.App.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;

namespace KBot.App.ViewModels;

public sealed class CavebotViewModel : ObservableObject
{
    private CavebotWaypoint? _selectedWaypoint;
    private string _newX = string.Empty;
    private string _newY = string.Empty;
    private string _newZ = string.Empty;
    private string _newName = string.Empty;
    private WaypointAction _newAction = WaypointAction.Walk;
    private string _feedback = "Importe uma rota JSON ou adicione seu primeiro waypoint.";
    private string _routeName = "Nova rota";
    private string? _currentFile;
    private readonly NativeService _nativeService = new();
    private readonly CavebotNavigator _navigator;

    public ObservableCollection<CavebotWaypoint> Waypoints { get; } = new();
    public Array Actions => Enum.GetValues<WaypointAction>();
    public CavebotWaypoint? SelectedWaypoint { get => _selectedWaypoint; set { Set(ref _selectedWaypoint, value); OnPropertyChanged(nameof(HasSelection)); } }
    public bool HasSelection => SelectedWaypoint is not null;
    public bool HasWaypoints => Waypoints.Count > 0;
    public int WaypointCount => Waypoints.Count;
    public string NewX { get => _newX; set => Set(ref _newX, value); }
    public string NewY { get => _newY; set => Set(ref _newY, value); }
    public string NewZ { get => _newZ; set => Set(ref _newZ, value); }
    public string NewName { get => _newName; set => Set(ref _newName, value); }
    public WaypointAction NewAction { get => _newAction; set => Set(ref _newAction, value); }
    public string Feedback { get => _feedback; private set => Set(ref _feedback, value); }
    public string RouteName { get => _routeName; private set => Set(ref _routeName, value); }
    public string NavigatorStatus => _navigator.Status;
    public bool CanStartRoute => _navigator is { IsRunning: false } && HasWaypoints;
    public bool RouteRunning => _navigator.IsRunning;

    public ICommand ImportCommand { get; private set; } = null!;
    public ICommand SaveCommand { get; private set; } = null!;
    public ICommand AddCommand { get; private set; } = null!;
    public ICommand RemoveCommand { get; private set; } = null!;
    public ICommand MoveUpCommand { get; private set; } = null!;
    public ICommand MoveDownCommand { get; private set; } = null!;
    public ICommand StepUpCommand { get; private set; } = null!;
    public ICommand StepDownCommand { get; private set; } = null!;
    public ICommand StepLeftCommand { get; private set; } = null!;
    public ICommand StepRightCommand { get; private set; } = null!;
    public ICommand StartRouteCommand { get; private set; } = null!;
    public ICommand StopRouteCommand { get; private set; } = null!;

    public CavebotViewModel()
    {
        _navigator = new CavebotNavigator(_nativeService);
        _navigator.Changed += OnNavigatorChanged;
        InitializeCommands();
    }

    public void Dispose() => _navigator.Dispose();

    private void InitializeCommands()
    {
        ImportCommand = new RelayCommand(_ => Import());
        SaveCommand = new RelayCommand(_ => Save());
        AddCommand = new RelayCommand(_ => Add());
        RemoveCommand = new RelayCommand(_ => Remove());
        MoveUpCommand = new RelayCommand(_ => Move(-1));
        MoveDownCommand = new RelayCommand(_ => Move(1));
        StepUpCommand = new RelayCommand(async _ => await StepAsync("UP"));
        StepDownCommand = new RelayCommand(async _ => await StepAsync("DOWN"));
        StepLeftCommand = new RelayCommand(async _ => await StepAsync("LEFT"));
        StepRightCommand = new RelayCommand(async _ => await StepAsync("RIGHT"));
        StartRouteCommand = new RelayCommand(_ => StartRouteAsync(), _ => CanStartRoute);
        StopRouteCommand = new RelayCommand(_ => StopRoute(), _ => _navigator.IsRunning);
        RaiseRouteState();
    }

    private void RaiseRouteState()
    {
        OnPropertyChanged(nameof(NavigatorStatus));
        OnPropertyChanged(nameof(CanStartRoute));
        OnPropertyChanged(nameof(RouteRunning));
        ((RelayCommand)StartRouteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopRouteCommand).RaiseCanExecuteChanged();
    }

    private void OnNavigatorChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.InvokeAsync(RaiseRouteState);
        else RaiseRouteState();
    }

    private void StartRouteAsync()
    {
        if (_navigator.IsRunning || !HasWaypoints) return;
        Feedback = "Iniciando rota...";
        _ = _navigator.StartAsync(Waypoints.ToList());
    }

    private void StopRoute()
    {
        if (!_navigator.IsRunning) return;
        _navigator.Stop();
        Feedback = "Solicitando parada da rota...";
    }

    private async Task StepAsync(string key)
    {
        Feedback = $"Enviando passo {key} para o cliente...";
        var sent = await _nativeService.SendKeyAsync(key);
        Feedback = sent
            ? $"Passo {key} enviado. O cliente precisa estar aberto e focado no controle de teclado."
            : "Não foi possível enviar o passo. Inicie o núcleo e abra o PokeAlliance primeiro.";
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "Rotas JSON (*.json)|*.json", Title = "Importar rota" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var loaded = CavebotScriptService.Parse(File.ReadAllText(dialog.FileName));
            Waypoints.Clear();
            foreach (var waypoint in loaded) Waypoints.Add(waypoint);
            SelectedWaypoint = null;
            _currentFile = null;
            RouteName = Path.GetFileNameWithoutExtension(dialog.FileName);
            RefreshCount();
            Feedback = $"{WaypointCount} waypoints importados. Revise a rota antes de salvar no formato KBot.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or InvalidOperationException or InvalidDataException)
        {
            Feedback = $"Não foi possível importar: {ex.Message}";
        }
    }

    private void Save()
    {
        if (!HasWaypoints) { Feedback = "Adicione pelo menos um waypoint antes de salvar."; return; }
        var dialog = new SaveFileDialog
        {
            Filter = "Rotas KBot (*.json)|*.json",
            Title = "Salvar rota",
            FileName = _currentFile is null ? $"{RouteName}.json" : Path.GetFileName(_currentFile),
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, CavebotScriptService.Serialize(Waypoints));
            _currentFile = dialog.FileName;
            RouteName = Path.GetFileNameWithoutExtension(dialog.FileName);
            Feedback = $"Rota salva com {WaypointCount} waypoints.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Feedback = $"Não foi possível salvar: {ex.Message}";
        }
    }

    private void Add()
    {
        if (!int.TryParse(NewX, out var x) || !int.TryParse(NewY, out var y) || !int.TryParse(NewZ, out var z))
        {
            Feedback = "Informe coordenadas X, Y e Z válidas.";
            return;
        }
        var waypoint = new CavebotWaypoint { Number = Waypoints.Count + 1, Name = NewName.Trim(), X = x, Y = y, Z = z, Action = NewAction };
        Waypoints.Add(waypoint);
        SelectedWaypoint = waypoint;
        NewName = string.Empty;
        RefreshCount();
        Feedback = $"Waypoint {waypoint.Number} adicionado. Salve a rota para guardar as alterações.";
    }

    private void Remove()
    {
        if (SelectedWaypoint is null) return;
        Waypoints.Remove(SelectedWaypoint);
        SelectedWaypoint = null;
        Renumber();
        Feedback = "Waypoint removido. Salve a rota para guardar as alterações.";
    }

    private void Move(int direction)
    {
        if (SelectedWaypoint is null) return;
        int index = Waypoints.IndexOf(SelectedWaypoint);
        int destination = index + direction;
        if (destination < 0 || destination >= Waypoints.Count) return;
        Waypoints.Move(index, destination);
        Renumber();
        Feedback = "Ordem atualizada. Salve a rota para guardar as alterações.";
    }

    private void Renumber()
    {
        for (int i = 0; i < Waypoints.Count; i++) Waypoints[i].Number = i + 1;
        RefreshCount();
    }

    private void RefreshCount()
    {
        OnPropertyChanged(nameof(HasWaypoints));
        OnPropertyChanged(nameof(WaypointCount));
        RaiseRouteState();
    }
}
