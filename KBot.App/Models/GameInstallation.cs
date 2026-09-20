using System.IO;

namespace KBot.App.Models;

public sealed class GameInstallation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public string LauncherPath { get; set; } = string.Empty;
    public List<string> GameExecutableNames { get; set; } = new();
    public string? LastGameExecutable { get; set; }
    // Kept for clients.json migration.
    public string ExecutablePath { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public DateTime? LastUsedAt { get; set; }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(LauncherPath)) LauncherPath = ExecutablePath;
        if (string.IsNullOrWhiteSpace(InstallDirectory))
            InstallDirectory = !string.IsNullOrWhiteSpace(WorkingDirectory) ? WorkingDirectory : Path.GetDirectoryName(LauncherPath) ?? string.Empty;
        GameExecutableNames ??= new();
        if (GameExecutableNames.Count == 0 && !string.IsNullOrWhiteSpace(LastGameExecutable))
            GameExecutableNames.Add(Path.GetFileName(LastGameExecutable));
        var executableName = Path.GetFileNameWithoutExtension(LauncherPath);
        if (string.IsNullOrWhiteSpace(Name) || string.Equals(Name, executableName, StringComparison.OrdinalIgnoreCase))
            Name = FriendlyName(LauncherPath, InstallDirectory);
    }

    public static string FriendlyName(string launcherPath, string installDirectory)
    {
        var stem = Path.GetFileNameWithoutExtension(launcherPath);
        var name = System.Text.RegularExpressions.Regex.Replace(stem, @"(?i)[\s_-]*launcher$", "").Trim(' ', '_', '-');
        if (name.Length == 0 || name.Equals("OT", StringComparison.OrdinalIgnoreCase) || name.Equals("Client", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileName(installDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? stem : name.Replace('_', ' ');
    }
}
