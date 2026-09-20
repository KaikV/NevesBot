using KBot.App.Models;
using System.Windows.Media.Imaging;

namespace KBot.App.Services;

public interface ICharacterSessionDetector
{
    CharacterDetection Detect(GameSession game, NativeStatus? nativeStatus);
    void Reset();
}

public sealed record CharacterDetection(
    CharacterPresence State, double Confidence, CharacterDetectionSource Source,
    string ReaderStatus, string ReaderMessage, string VisionStatus,
    int VisionSignals, int VisionSignalTotal, BitmapSource? Frame,
    IReadOnlyList<VisionRegion> Regions,
    CharacterDetectionStatus DetectionStatus = CharacterDetectionStatus.Evaluating,
    DateTime? LastConfirmedInGame = null);

public sealed class CharacterSessionDetector : ICharacterSessionDetector
{
    private readonly ClientStateDetector _client = new();
    private readonly VisionInGameDetector _vision = new();
    private readonly CharacterStateAggregator _aggregator = new();

    public CharacterDetection Detect(GameSession game, NativeStatus? nativeStatus)
    {
        var client = _client.Detect(nativeStatus, game.Pid);
        var vision = _vision.Detect(game);
        var decision = _aggregator.Update(client, vision);
        return new CharacterDetection(decision.State, decision.Confidence, decision.Source,
            nativeStatus?.ReaderStatus ?? "UNAVAILABLE",
            nativeStatus is null ? "Sem resposta do núcleo nativo." :
                nativeStatus.ReaderMessage ?? "Núcleo antigo não informou o motivo; atualize KBot.Native.exe.",
            vision.Status, vision.SignalCount, vision.SignalTotal, vision.Frame, vision.Regions,
            decision.DetectionStatus, decision.LastConfirmedInGame);
    }

    public void Reset() => _aggregator.Reset();
}

public sealed record ClientStateEvidence(CharacterPresence State, bool Reliable, double Confidence);

public sealed class ClientStateDetector
{
    public ClientStateEvidence Detect(NativeStatus? status, int expectedPid)
    {
        if (status?.Pid != expectedPid || status.ReaderStatus != "READY")
            return new(CharacterPresence.Unknown, false, 0);
        return status.CharacterState?.ToUpperInvariant() switch
        {
            "INGAME" => new(CharacterPresence.InGame, true, 0.99),
            "LOGINSCREEN" => new(CharacterPresence.LoginScreen, true, 0.99),
            "CHARACTERSELECTION" => new(CharacterPresence.CharacterSelection, true, 0.99),
            "LOADING" => new(CharacterPresence.Loading, true, 0.99),
            "DISCONNECTED" => new(CharacterPresence.Disconnected, true, 0.99),
            _ => new(CharacterPresence.Unknown, false, 0)
        };
    }
}
