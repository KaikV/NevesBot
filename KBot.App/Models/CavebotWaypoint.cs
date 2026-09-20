using System.ComponentModel;

namespace KBot.App.Models;

public enum WaypointAction
{
    Walk,
    Wait,
    Talk,
    Use,
    StopAttacker,
    StartAttacker,
    OrderPokemon
}

public sealed class CavebotWaypoint : INotifyPropertyChanged
{
    private int _number;
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Number
    {
        get => _number;
        set
        {
            if (_number == value) return;
            _number = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Number)));
        }
    }
    public string Name { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public WaypointAction Action { get; init; }
    public string Position => $"{X}, {Y}, {Z}";
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Action.ToString() : Name;
}
