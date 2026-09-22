using KBot.App.Models;

namespace KBot.App.Engine.Sensors
{
    /// <summary>
    /// Thin sensor over the existing structured read (native named pipe).
    /// Does not duplicate ClientReader or IPC; it only adapts the result into
    /// a standard <see cref="SensorValue{PositionValue}"/>.
    /// </summary>
    public sealed class StructuredPositionSensor : ISensor<PositionValue>
    {
        public const string NameConst = "StructuredPositionSensor";

        private readonly Func<Task<NativeStatus?>> _read;

        public string Name => NameConst;
        public SensorTier Tier => SensorTier.Normal;

        public StructuredPositionSensor(Func<Task<NativeStatus?>> read)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
        }

        public async Task<SensorValue<PositionValue>> ReadAsync(CancellationToken ct = default)
        {
            NativeStatus? status;
            try
            {
                status = await _read().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new SensorValue<PositionValue>
                {
                    Value = null,
                    CapturedAt = Environment.TickCount64,
                    Source = Name,
                    Confidence = 0,
                    IsValid = false,
                    Health = SensorHealth.Unavailable,
                    Error = ex.Message
                };
            }

            if (status is null || !status.ClientFound)
            {
                return SensorValue<PositionValue>.Unknown(Name);
            }

            if (!status.HasPosition)
            {
                return new SensorValue<PositionValue>
                {
                    Value = null,
                    CapturedAt = Environment.TickCount64,
                    Source = Name,
                    Confidence = 0,
                    IsValid = false,
                    Health = SensorHealth.Degraded,
                    Error = "client connected but position unreadable"
                };
            }

            return SensorValue<PositionValue>.Of(new PositionValue(status.PosX, status.PosY, status.PosZ), Name);
        }
    }
}
