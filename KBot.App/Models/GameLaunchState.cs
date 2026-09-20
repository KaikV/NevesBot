namespace KBot.App.Models;

public enum GameLaunchState
{
    NotConfigured, Ready, StartingLauncher, LauncherRunning, WaitingForGame,
    GameDetected, Connecting, Connected, Disconnected, Error
}
