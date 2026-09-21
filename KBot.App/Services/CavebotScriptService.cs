using KBot.App.Models;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace KBot.App.Services;

public static class CavebotScriptService
{
    public static IReadOnlyList<CavebotWaypoint> Parse(string json) => ParseDocument(json).Waypoints;

    public static CavebotRouteDocument ParseDocument(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0) throw new InvalidDataException("A rota não contém dados.");
            root = root[0];
        }

        if (root.ValueKind != JsonValueKind.Object ||
            !TryGet(root, "waypoints", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Arquivo de rota sem lista de waypoints.");

        var waypoints = new List<CavebotWaypoint>();
        foreach (var entry in entries.EnumerateArray())
        {
            using var innerDocument = entry.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(entry.GetString() ?? string.Empty)
                : null;
            var item = innerDocument?.RootElement ?? entry;
            if (item.ValueKind != JsonValueKind.Object ||
                !TryGet(item, "position", out var position) ||
                !TryGet(item, "action", out var actionElement))
                throw new InvalidDataException($"Waypoint {waypoints.Count + 1} incompleto.");

            var (x, y, z) = ReadPosition(position);
            if (actionElement.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Ação inválida no waypoint {waypoints.Count + 1}.");
            var actionText = actionElement.GetString();
            if (!Enum.TryParse<WaypointAction>(actionText, true, out var action) || !Enum.IsDefined(action))
                throw new InvalidDataException($"Ação inválida no waypoint {waypoints.Count + 1}: {actionText}.");

            var name = ReadString(item, "name");
            var argument = ReadString(item, "argument");
            var delayMs = ReadInt(item, "delayMs", 0);
            var explicitOrder = ReadInt(item, "order", waypoints.Count + 1);
            waypoints.Add(new CavebotWaypoint
            {
                Number = explicitOrder > 0 ? explicitOrder : waypoints.Count + 1,
                Name = name,
                X = x,
                Y = y,
                Z = z,
                Action = action,
                Argument = argument,
                DelayMs = Math.Clamp(delayMs, 0, 600_000)
            });
        }

        var ordered = waypoints.OrderBy(w => w.Number).ToList();
        for (var i = 0; i < ordered.Count; i++) ordered[i].Number = i + 1;
        return new CavebotRouteDocument
        {
            SchemaVersion = Math.Max(1, ReadInt(root, "schemaVersion", 1)),
            RouteId = ReadString(root, "routeId") is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N"),
            Name = ReadString(root, "name") is { Length: > 0 } routeName ? routeName : "Rota importada",
            Version = Math.Max(1, ReadInt(root, "version", 1)),
            Waypoints = ordered
        };
    }

    public static string Serialize(IEnumerable<CavebotWaypoint> waypoints) => Serialize(new CavebotRouteDocument
    {
        Waypoints = waypoints.ToList()
    });

    public static string Serialize(CavebotRouteDocument route) => JsonSerializer.Serialize(new
    {
        schemaVersion = 2,
        format = "kbot-route-v2",
        routeId = route.RouteId,
        name = route.Name,
        version = Math.Max(1, route.Version),
        waypoints = route.Waypoints.Select((w, index) => new
        {
            order = index + 1,
            name = w.Name,
            position = new { x = w.X, y = w.Y, z = w.Z },
            action = w.Action.ToString(),
            argument = w.Argument,
            delayMs = Math.Max(0, w.DelayMs)
        })
    }, new JsonSerializerOptions { WriteIndented = true });

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string ReadString(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement element, string name, int fallback) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : fallback;

    private static (int X, int Y, int Z) ReadPosition(JsonElement position)
    {
        if (position.ValueKind == JsonValueKind.String)
        {
            var parts = (position.GetString() ?? string.Empty).Split(',');
            if (parts.Length == 3 &&
                int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
                int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) &&
                int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var z))
                return (x, y, z);
        }
        else if (position.ValueKind == JsonValueKind.Object &&
                 TryGet(position, "x", out var xElement) && xElement.TryGetInt32(out var x) &&
                 TryGet(position, "y", out var yElement) && yElement.TryGetInt32(out var y) &&
                 TryGet(position, "z", out var zElement) && zElement.TryGetInt32(out var z))
            return (x, y, z);

        throw new InvalidDataException("Waypoint com posição inválida.");
    }
}
