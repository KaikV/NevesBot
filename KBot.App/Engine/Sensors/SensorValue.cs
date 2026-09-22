using KBot.App.BotBrain;

namespace KBot.App.Engine.Sensors
{
    public enum SensorHealth
    {
        Healthy,
        Degraded,
        Unavailable
    }

    public sealed record SensorValue<T>
    {
        public T? Value { get; init; }
        public long CapturedAt { get; init; }
        public string Source { get; init; } = "";
        public double Confidence { get; init; } = 1.0;
        public bool IsValid { get; init; }
        public SensorHealth Health { get; init; } = SensorHealth.Healthy;
        public string? Error { get; init; }

        public static SensorValue<T> Unknown(string source) => new()
        {
            Value = default,
            CapturedAt = Environment.TickCount64,
            Source = source,
            Confidence = 0,
            IsValid = false,
            Health = SensorHealth.Unavailable,
            Error = "sensor returned no data"
        };

        public static SensorValue<T> Of(T value, string source, double confidence = 1.0) => new()
        {
            Value = value,
            CapturedAt = Environment.TickCount64,
            Source = source,
            Confidence = confidence,
            IsValid = true,
            Health = SensorHealth.Healthy
        };

        public TimeSpan Age(long nowMs) => TimeSpan.FromMilliseconds(nowMs - CapturedAt);
    }

    public sealed record PositionValue(int X, int Y, int Z);

    public sealed record PresenceValue(bool InGame, double Confidence, string Source);

    /// <summary>
    /// The full, honest result of a creature scan. Carries <see cref="ScanResult"/>
    /// so downstream sees whether a read actually happened (<see cref="ScanResult.HasRead"/>),
    /// never mistaking "no read" for "empty screen".
    /// </summary>
    public sealed record CreaturesValue(ScanResult Scan);
}
