using System.Diagnostics;
using KBot.App.Engine.Sensors;

namespace KBot.App.Engine.Scheduler
{
    /// <summary>
    /// Runs every registered sensor on its own cadence. Sensors are grouped by
    /// <see cref="SensorTier"/>; exactly ONE background task per tier loops over its sensors,
    /// awaiting each read and publishing into the shared <see cref="SensorSampleStore"/>.
    ///
    /// Design notes:
    ///  - Tiers decouple cadence: fast (HP/target), normal (creatures/position), slow (inventory).
    ///  - One task per tier (not one per sensor) bounds thread count and keeps related reads
    ///    time-correlated within a tier.
    ///  - A slow/stuck sensor delays only its own tier loop; it cannot starve another tier.
    ///  - The delay is injected (<see cref="DelayFn"/>) so tests can drive many ticks without
    ///    real waiting and without flaky timing.
    /// </summary>
    public sealed class SensorScheduler : IAsyncDisposable
    {
        public delegate Task DelayFn(int ms, CancellationToken ct);

        private readonly Dictionary<SensorTier, List<SensorRegistration>> _byTier;
        private readonly SensorSampleStore _store;
        private readonly DelayFn _delay;
        private readonly int _fastMs;
        private readonly int _normalMs;
        private readonly int _slowMs;

        private CancellationTokenSource? _cts;
        private readonly List<Task> _tasks = new();
        private readonly object _state = new();
        private bool _running;

        // last successful read + duration per sensor, for fusion health / UI
        private readonly Dictionary<string, long> _lastTickMs = new();
        private readonly Dictionary<string, long> _lastDurationMs = new();

        public SensorScheduler(
            IEnumerable<SensorRegistration> sensors,
            SensorSampleStore store,
            DelayFn? delay = null,
            int fastMs = 150, int normalMs = 300, int slowMs = 1500)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _delay = delay ?? DefaultDelay;
            _fastMs = fastMs; _normalMs = normalMs; _slowMs = slowMs;

            _byTier = new Dictionary<SensorTier, List<SensorRegistration>>
            {
                [SensorTier.Fast] = new(),
                [SensorTier.Normal] = new(),
                [SensorTier.Slow] = new()
            };
            foreach (var s in sensors)
                _byTier[s.Tier].Add(s);
        }

        public static async Task DefaultDelay(int ms, CancellationToken ct) => await Task.Delay(ms, ct);

        public void Start()
        {
            lock (_state)
            {
                if (_running) return;
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;

                _tasks.Add(RunTierAsync(SensorTier.Fast, _fastMs, ct));
                _tasks.Add(RunTierAsync(SensorTier.Normal, _normalMs, ct));
                _tasks.Add(RunTierAsync(SensorTier.Slow, _slowMs, ct));
                _running = true;
            }
        }

        private async Task RunTierAsync(SensorTier tier, int intervalMs, CancellationToken ct)
        {
            var sensors = _byTier[tier];
            if (sensors.Count == 0) return;

            while (!ct.IsCancellationRequested)
            {
                foreach (var s in sensors)
                {
                    if (ct.IsCancellationRequested) return;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var sample = await s.Read(ct).ConfigureAwait(false);
                        _store.Publish(s.Name, sample);
                        _lastTickMs[s.Name] = Environment.TickCount64;
                        _lastDurationMs[s.Name] = sw.ElapsedMilliseconds;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        // A broken sensor must never kill its tier loop or another tier.
                        _lastDurationMs[s.Name] = sw.ElapsedMilliseconds;
                        Error?.Invoke(s.Name, ex);
                    }
                }

                try { await _delay(intervalMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        public event Action<string, Exception>? Error;

        /// True since the sensor's last read is within the given window.
        public bool IsFresh(string name, long windowMs)
        {
            lock (_state)
                return _lastTickMs.TryGetValue(name, out var t) && (Environment.TickCount64 - t) <= windowMs;
        }

        public long LastDuration(string name)
        {
            lock (_state)
                return _lastDurationMs.TryGetValue(name, out var d) ? d : 0;
        }

        public async ValueTask DisposeAsync()
        {
            CancellationTokenSource? cts;
            lock (_state)
            {
                if (!_running) { _running = false; return; }
                cts = _cts;
                _running = false;
            }
            cts?.Cancel();
            await Task.WhenAll(_tasks).ConfigureAwait(false);
            cts?.Dispose();
            _tasks.Clear();
        }
    }
}
