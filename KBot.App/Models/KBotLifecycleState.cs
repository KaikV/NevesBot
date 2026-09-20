namespace KBot.App.Models;

public enum KBotLifecycleState
{
    Booting,
    ClientNotConfigured,
    LaunchingClient,
    WaitingForProcess,
    ProcessDetected,
    WaitingForWindow,
    ClientConnected,
    WaitingForLogin,
    WaitingForCharacter,
    Ready,
    Disconnected,
    Error
}
