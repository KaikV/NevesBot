using KBot.App.Models;

namespace KBot.App.Services;

public sealed record CharacterStateDecision(
    CharacterPresence State, double Confidence, CharacterDetectionSource Source,
    CharacterDetectionStatus DetectionStatus, DateTime? LastConfirmedInGame);

public sealed class CharacterStateAggregator
{
    private CharacterPresence _confirmed = CharacterPresence.Unknown;
    private CharacterPresence _candidate = CharacterPresence.Unknown;
    private int _consecutive;
    private int _misses;
    private double _confirmedConfidence;
    private CharacterDetectionSource _confirmedSource = CharacterDetectionSource.None;
    private DateTime? _lastConfirmedInGame;

    public CharacterStateDecision Update(ClientStateEvidence client, VisionEvidence vision)
    {
        if (vision.CaptureStatus == VisionCaptureStatus.CaptureUnavailable)
        {
            // A missing frame is not a negative observation. Break an incomplete
            // streak, but preserve the last confirmed character state and time.
            _candidate = CharacterPresence.Unknown;
            _consecutive = 0;
            _misses = 0;
            return new(_confirmed, _confirmedConfidence, _confirmedSource,
                CharacterDetectionStatus.CaptureUnavailable, _lastConfirmedInGame);
        }

        var state = CharacterPresence.Unknown;
        var confidence = 0d;
        var source = CharacterDetectionSource.None;
        if (client.Reliable)
        {
            state = client.State;
            confidence = client.Confidence;
            source = CharacterDetectionSource.ClientReader;
            if (vision.State == state)
            {
                confidence = Math.Max(confidence, vision.Confidence);
                source = CharacterDetectionSource.Combined;
            }
            else if (vision.State != CharacterPresence.Unknown && vision.State != state)
            {
                state = CharacterPresence.Unknown;
                confidence = 0;
                source = CharacterDetectionSource.None;
            }
        }
        else if (vision.State != CharacterPresence.Unknown)
        {
            state = vision.State;
            confidence = vision.Confidence;
            source = CharacterDetectionSource.Vision;
        }

        if (state == _confirmed)
        {
            _misses = 0;
            _candidate = CharacterPresence.Unknown;
            _consecutive = 0;
            if (state != CharacterPresence.Unknown)
            {
                _confirmedConfidence = confidence;
                _confirmedSource = source;
                if (state == CharacterPresence.InGame) _lastConfirmedInGame = DateTime.Now;
            }
        }
        else if (state == CharacterPresence.Unknown)
        {
            _candidate = CharacterPresence.Unknown;
            _consecutive = 0;
            // A disagreement with a visible InGame HUD is inconclusive, not a
            // negative visual reading.
            if (vision.State == CharacterPresence.InGame) _misses = 0;
            else if (_confirmed != CharacterPresence.Unknown && ++_misses >= (_confirmed == CharacterPresence.InGame ? 4 : 2))
            {
                _confirmed = CharacterPresence.Unknown;
                _confirmedConfidence = 0;
                _confirmedSource = CharacterDetectionSource.None;
            }
        }
        else
        {
            _misses = 0;
            _consecutive = state == _candidate ? _consecutive + 1 : 1;
            _candidate = state;
            var required = _confirmed == CharacterPresence.InGame ? 4 : 3;
            if (_consecutive >= required)
            {
                _confirmed = state;
                _confirmedConfidence = confidence;
                _confirmedSource = source;
                if (state == CharacterPresence.InGame) _lastConfirmedInGame = DateTime.Now;
                _candidate = CharacterPresence.Unknown;
                _consecutive = 0;
            }
        }

        if (_confirmed == CharacterPresence.Unknown)
            return new(_confirmed, 0, CharacterDetectionSource.None,
                state == CharacterPresence.InGame ? CharacterDetectionStatus.Evaluating :
                    CharacterDetectionStatus.NotInGame, _lastConfirmedInGame);
        if (state == _confirmed)
            return new(_confirmed, confidence, source,
                state == CharacterPresence.InGame ? CharacterDetectionStatus.Confirmed :
                    CharacterDetectionStatus.NotInGame, _lastConfirmedInGame);
        return new(_confirmed, _confirmed == CharacterPresence.InGame ? 0.5 : 0,
            CharacterDetectionSource.None,
            state == CharacterPresence.InGame ? CharacterDetectionStatus.Evaluating :
                CharacterDetectionStatus.NotInGame, _lastConfirmedInGame);
    }

    public void Reset()
    {
        _confirmed = CharacterPresence.Unknown;
        _candidate = CharacterPresence.Unknown;
        _consecutive = 0;
        _misses = 0;
        _confirmedConfidence = 0;
        _confirmedSource = CharacterDetectionSource.None;
        _lastConfirmedInGame = null;
    }
}
