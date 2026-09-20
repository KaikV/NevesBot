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
