using KBot.App.BotBrain;

namespace KBot.App.Engine.Sensors
{
    /// <summary>
    /// Visual creature sensor: raw frame → pixel extraction (first-pass) → reuse of the
    /// existing <see cref="ScreenScan.Analyze"/> classifier → standard SensorValue.
    ///
    /// Honesty contract: a failed/blank capture yields Unavailable (value null), NOT an
    /// empty list — "no read" must never be mistaken for "the world is empty".
    /// </summary>
    public sealed class VisualCreatureSensor : ISensor<CreaturesValue>
    {
        public const string NameConst = "VisualCreatureSensor";

        private readonly IFrameSource _frame;
        private readonly CreatureScanParams _params;

        public string Name => NameConst;
        public SensorTier Tier => SensorTier.Normal;

        public VisualCreatureSensor(IFrameSource frame, CreatureScanParams? parameters = null)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _params = parameters ?? CreatureFrameAnalyzer.Default;
        }

        public Task<SensorValue<CreaturesValue>> ReadAsync(CancellationToken ct = default)
        {
            var result = _frame.Capture();

            if (result.Outcome != CaptureOutcome.Ok || result.Frame is null)
            {
                var noRead = new SensorValue<CreaturesValue>
                {
                    Value = new CreaturesValue(ScreenScan.Empty()),
                    CapturedAt = Environment.TickCount64,
                    Source = Name,
                    Confidence = 0,
                    IsValid = false,
                    Health = SensorHealth.Unavailable,
                    Error = result.Error ?? "capture unavailable"
                };
                return Task.FromResult(noRead);
            }

            var creatures = CreatureFrameAnalyzer.Extract(result.Frame, _params);
            var scan = ScreenScan.Analyze(creatures);   // reuse the existing classifier

            var value = new SensorValue<CreaturesValue>
            {
                Value = new CreaturesValue(scan),
                CapturedAt = Environment.TickCount64,
                Source = Name,
                // Visual first-pass: tiles are detected, not OCR-read -> moderate confidence.
                Confidence = .6,
                IsValid = true,
                Health = SensorHealth.Healthy
            };
            return Task.FromResult(value);
        }
    }
}
