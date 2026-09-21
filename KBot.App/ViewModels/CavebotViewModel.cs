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

    // Offset-hunting (re-calibrate after a client update). The minimap shows the
    // exact position; typing it here scans the module for that int32 triple.
    private string _scanX = string.Empty;
    private string _scanY = string.Empty;
    private string _scanZ = string.Empty;
    private bool _scanRunning;

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
    public bool CanStartRoute => _navigator is { IsRunning: false };
    public bool RouteRunning => _navigator.IsRunning;

    public string ScanX { get => _scanX; set { Set(ref _scanX, value); OnPropertyChanged(nameof(CanScan)); } }
    public string ScanY { get => _scanY; set { Set(ref _scanY, value); OnPropertyChanged(nameof(CanScan)); } }
    public string ScanZ { get => _scanZ; set { Set(ref _scanZ, value); OnPropertyChanged(nameof(CanScan)); } }
    public string ScanResult { get; private set; } = "";
    public bool ScanRunning { get => _scanRunning; private set { Set(ref _scanRunning, value); OnPropertyChanged(nameof(CanScan)); } }
    public bool CanScan => !ScanRunning &&
        int.TryParse(ScanX, out int x) && int.TryParse(ScanY, out int y) && int.TryParse(ScanZ, out int z);

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
    public ICommand ScanCommand { get; private set; } = null!;

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
        ScanCommand = new RelayCommand(async _ => await ScanAsync());
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
        if (_navigator.IsRunning) return;
        Feedback = HasWaypoints ? "Iniciando rota..." : "Iniciando modo livre...";
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

    private async Task ScanAsync()
    {
        if (!int.TryParse(ScanX, out int x) || !int.TryParse(ScanY, out int y) || !int.TryParse(ScanZ, out int z))
        {
            ScanResult = "Digite as três coordenadas exatas do minimap (ex.: 2079 / 2037 / 7).";
            return;
        }

        ScanRunning = true;
        ScanResult = "Varrendo o módulo inteiro procurando essa tripla... pode levar uns segundos.";

        try
        {
            string? raw = await _nativeService.ScanForPositionAsync(x, y, z);
            ScanResult = FormatScanResult(raw, x, y, z);
        }
        finally
        {
            ScanRunning = false;
        }
    }

    private static string FormatScanResult(string? raw, int x, int y, int z)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "O núcleo não respondeu. Inicie o núcleo e garanta que o PokeAlliance está aberto.";

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            bool ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == System.Text.Json.JsonValueKind.True;
            long count = root.TryGetProperty("count", out var countEl) ? countEl.GetInt64() : 0;
            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : string.Empty;

            if (!ok)
                return $"Falha no varredura: {message}";

            if (count == 0)
                return $"Nenhuma tripla ({x},{y},{z}) encontrada. Confirme que as coordenadas do minimap estão exatas e tente em outro ponto do mapa.";

            if (!root.TryGetProperty("candidates", out var cands) || cands.ValueKind != System.Text.Json.JsonValueKind.Array)
                return $"Encontrada {count} ocorrência(s), mas a lista de endereços veio vazia.";

            var lines = new System.Collections.Generic.List<string> { $"Tripla ({x},{y},{z}) encontrada {count} vez(es). Endereços candidatos (offset da base):" };
            foreach (var c in cands.EnumerateArray())
                lines.Add($"  {c.GetString()}");
            lines.Add("");
            lines.Add("Teste: ande 1 passo e rode o scan de novo com a NOVA posição. O offset que SURVIVERE entre os dois scans é o correto.");
            return string.Join(Environment.NewLine, lines);
        }
        catch (System.Text.Json.JsonException)
        {
            return $"Resposta inesperada do núcleo:\n{raw}";
        }
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
