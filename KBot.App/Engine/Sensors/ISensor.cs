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
}
