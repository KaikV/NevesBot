namespace KBot.App.Models;

public sealed class CavebotRouteDocument
{
    public int SchemaVersion { get; init; } = 2;
    public string RouteId { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Nova rota";
    public int Version { get; init; } = 1;
    public IReadOnlyList<CavebotWaypoint> Waypoints { get; init; } = Array.Empty<CavebotWaypoint>();
}
