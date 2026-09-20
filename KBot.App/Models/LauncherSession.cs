namespace KBot.App.Models;

public sealed record LauncherSession(int LauncherPid, string LauncherPath, DateTime StartedAt);
