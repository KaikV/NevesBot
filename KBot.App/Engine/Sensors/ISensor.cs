namespace KBot.App.Engine.Sensors
{
    public enum SensorTier
    {
        Fast = 0,
        Normal = 1,
        Slow = 2
    }

    public interface ISensor<T>
    {
        string Name { get; }
        SensorTier Tier { get; }
        Task<SensorValue<T>> ReadAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// A type-erased sensor as the scheduler sees it: a name, a tier (its cadence), and an
    /// opaque read that returns the latest sample. Keeps the scheduler generic-free so it can
    /// drive heterogeneous sensors on a single per-tier task.
    /// </summary>
    public sealed record SensorRegistration(string Name, SensorTier Tier, Func<CancellationToken, Task<object?>> Read)
    {
        public static SensorRegistration From<T>(ISensor<T> sensor) =>
            new(sensor.Name, sensor.Tier, async ct => (object?)(await sensor.ReadAsync(ct)));
    }
}
