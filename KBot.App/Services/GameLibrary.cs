using KBot.App.Models;
using System.Text.Json;
using System.IO;

namespace KBot.App.Services;

public sealed class GameLibrary
{
    public static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KBot", "clients.json");

    public IReadOnlyList<GameInstallation> Load()
    {
        if (!File.Exists(StorePath)) return Array.Empty<GameInstallation>();
        try
        {
            var clients = JsonSerializer.Deserialize<List<GameInstallation>>(File.ReadAllText(StorePath)) ?? new();
            foreach (var client in clients) client.Normalize();
            return clients.Where(IsValid).OrderByDescending(c => c.LastUsedAt).ToList();
        }
        catch (JsonException) { return Array.Empty<GameInstallation>(); }
        catch (IOException) { return Array.Empty<GameInstallation>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<GameInstallation>(); }
    }

    public GameInstallation? LastUsed() => Load().FirstOrDefault();

    public void Save(GameInstallation client)
    {
        client.Normalize();
        var clients = Load().Where(c => !string.Equals(c.Id, client.Id, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(c.LauncherPath, client.LauncherPath, StringComparison.OrdinalIgnoreCase)).ToList();
        client.LastUsedAt = DateTime.Now;
        clients.Insert(0, client);
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(clients, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool IsValid(GameInstallation client) =>
        !string.IsNullOrWhiteSpace(client.Name) && File.Exists(client.LauncherPath);
}
