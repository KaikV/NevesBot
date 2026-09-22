using KBot.App.Models;

namespace KBot.App.Engine.Sensors
{
    /// <summary>
    /// Thin presence probe — the minimal slice of the existing detection pipeline the
    /// sensor needs. Keeping it free of WPF types lets the sensor be tested headless while
    /// production feeds it from <c>CharacterSessionDetector</c> / <c>CharacterSession</c>.
    /// </summary>
    public sealed record PresenceProbe(bool InGame, double Confidence, bool CaptureAvailable, string Source);

    /// <summary>
    /// Presence sensor. Wraps the EXISTING client+vision detection path
    /// (<c>CharacterSessionDetector</c>) behind a standard SensorValue — no re-implementation.
    /// A frame that could not be captured reports Unavailable, never a false InGame/Out.
    /// </summary>
    public sealed class VisionPresenceSensor : ISensor<PresenceValue>
    {
        public const string NameConst = "VisionPresenceSensor";

        private readonly Func<PresenceProbe> _probe;

        public string Name => NameConst;
        public SensorTier Tier => SensorTier.Normal;

        public VisionPresenceSensor(Func<PresenceProbe> probe)
        {
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        }

        public Task<SensorValue<PresenceValue>> ReadAsync(CancellationToken ct = default)
        {
            PresenceProbe probe;
            try { probe = _probe(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Task.FromResult(new SensorValue<PresenceValue>
                {
                    Value = null,
                    CapturedAt = Environment.TickCount64,
                    Source = Name,
                    IsValid = false,
                    Health = SensorHealth.Unavailable,
                    Error = ex.Message
                });
            }

            if (!probe.CaptureAvailable)
            {
                return Task.FromResult(new SensorValue<PresenceValue>
                {
                    Value = new PresenceValue(probe.InGame, probe.Confidence, probe.Source),
                    CapturedAt = Environment.TickCount64,
                    Source = Name,
                    Confidence = 0,
                    IsValid = false,
                    Health = SensorHealth.Degraded,
                    Error = "presence source temporarily unavailable"
                });
            }

            return Task.FromResult(SensorValue<PresenceValue>.Of(
                new PresenceValue(probe.InGame, probe.Confidence, probe.Source),
                Name,
                probe.Confidence));
        }
    }
}
