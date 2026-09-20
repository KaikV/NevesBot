using KBot.App.Models;
using System.Diagnostics;
using System.IO;

namespace KBot.App.Services;

public sealed class GameLauncher
{
    public LauncherSession? FindRunning(GameInstallation client)
    {
        client.Normalize();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(client.LauncherPath)))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.Equals(path, client.LauncherPath, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    catch (System.ComponentModel.Win32Exception) { /* Exact executable name is still known. */ }
                    catch (UnauthorizedAccessException) { }
                    return new LauncherSession(process.Id, client.LauncherPath, process.StartTime);
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        return null;
    }

    public LauncherSession Launch(GameInstallation client)
    {
        client.Normalize();
        var process = Process.Start(new ProcessStartInfo(client.LauncherPath)
        {
            WorkingDirectory = client.InstallDirectory,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("O launcher não iniciou.");
        using (process) return new LauncherSession(process.Id, client.LauncherPath, DateTime.Now);
    }
}
