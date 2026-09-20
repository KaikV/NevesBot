namespace KBot.App.Models;

public enum CharacterPresence
{
    Unknown,
    LoginScreen,
    NotLoggedIn = LoginScreen,
    CharacterSelection,
    Loading,
    InGame,
    Disconnected
}

public enum CharacterDetectionSource { None, ClientReader, Vision, Combined }
public enum CharacterDetectionStatus { Evaluating, Confirmed, NotInGame, CaptureUnavailable }

public sealed record CharacterSession(
    int GameProcessId, CharacterPresence State, DateTime ObservedAt,
    double Confidence = 0, CharacterDetectionSource DetectionSource = CharacterDetectionSource.None,
    CharacterDetectionStatus DetectionStatus = CharacterDetectionStatus.Evaluating,
    DateTime? LastConfirmedInGame = null)
{
    public bool IsInGame => State == CharacterPresence.InGame;
    public DateTime LastUpdated => ObservedAt;
}
