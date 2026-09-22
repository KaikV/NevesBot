using KBot.App.Engine.Sensors;

namespace KBot.App.Engine.Scheduler
{
    /// <summary>
    /// Latest-value store: one slot per sensor, last read wins. Written by the scheduler's
    /// tier tasks, read by fusion. Volatile reference swaps make this effectively lock-free —
    /// a reader always sees either the previous or the new sample, never torn state.
    /// </summary>
    public sealed class SensorSampleStore
    {
        private readonly Dictionary<string, VolatileSlot> _slots = new();

        public SensorSampleStore(IEnumerable<SensorRegistration> sensors)
        {
            foreach (var s in sensors)
                _slots[s.Name] = new VolatileSlot();
        }

        public void Publish(string name, object? sample)
        {
            if (_slots.TryGetValue(name, out var slot))
                slot.Value = sample;
        }

        public bool TryGet<T>(string name, out T? sample) where T : class
        {
            if (_slots.TryGetValue(name, out var slot))
            {
                sample = slot.Value as T;
                return true;
            }
            sample = null;
            return false;
        }

        /// All currently-published samples keyed by sensor name (for fusion / diagnostics).
        public IReadOnlyDictionary<string, object?> Snapshot()
        {
            var result = new Dictionary<string, object?>(_slots.Count);
            foreach (var (name, slot) in _slots)
                result[name] = slot.Value;
            return result;
        }

        private sealed class VolatileSlot
        {
            public volatile object? Value;
        }
    }
}
