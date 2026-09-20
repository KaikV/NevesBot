using KBot.App.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KBot.App.Services;

public static class CavebotScriptService
{
    public static IReadOnlyList<CavebotWaypoint> Parse(string json)
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
            if (!Enum.TryParse<WaypointAction>(actionText, true, out var action) ||
                !Enum.IsDefined(action))
                throw new InvalidDataException($"Ação inválida no waypoint {waypoints.Count + 1}: {actionText}.");

            string name = TryGet(item, "name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
            waypoints.Add(new CavebotWaypoint
            {
                Number = waypoints.Count + 1,
                Name = name,
                X = x,
                Y = y,
                Z = z,
                Action = action
            });
        }
        return waypoints;
    }

    public static string Serialize(IEnumerable<CavebotWaypoint> waypoints) =>
        JsonSerializer.Serialize(new
        {
            format = "kbot-route-v1",
            waypoints = waypoints.Select(w => new
            {
                name = w.Name,
                position = new { x = w.X, y = w.Y, z = w.Z },
                action = w.Action.ToString()
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
                 TryGet(position, "x", out var xElement) && xElement.ValueKind == JsonValueKind.Number && xElement.TryGetInt32(out var x) &&
                 TryGet(position, "y", out var yElement) && yElement.ValueKind == JsonValueKind.Number && yElement.TryGetInt32(out var y) &&
                 TryGet(position, "z", out var zElement) && zElement.ValueKind == JsonValueKind.Number && zElement.TryGetInt32(out var z))
            return (x, y, z);

        throw new InvalidDataException("Waypoint com posição inválida.");
    }
}
