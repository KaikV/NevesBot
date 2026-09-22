using System.Collections.Concurrent;

namespace KBot.App.Engine.Events
{
    /// <summary>
    /// Engine-internal event bus for <see cref="GameDelta"/>s. Modules subscribe to the specific
    /// delta types they care about (or to everything); the engine publishes the deltas a
    /// <see cref="DeltaEngine"/> produces each tick.
    ///
    /// Kept deliberately tiny and dependency-free (no UI, no WPF) so it runs in headless tests. It
    /// keeps a bounded ring of recent deltas for diagnostics/replay and swallows handler exceptions
    /// so one buggy subscriber can never stall the tick or the other subscribers.
    /// </summary>
    public sealed class EventBus
    {
        private const int LogCapacity = 200;
        private readonly ConcurrentQueue<GameDelta> _log = new();
        private readonly List<Sub> _subs = new();
        private readonly object _gate = new();

        /// Subscribe to EVERY delta.
        public void Subscribe(Action<GameDelta> handler) => Add(null, handler);

        /// Subscribe only to the given delta types.
        public void Subscribe(IEnumerable<GameDeltaType> types, Action<GameDelta> handler)
            => Add(new HashSet<GameDeltaType>(types), handler);

        private void Add(HashSet<GameDeltaType>? types, Action<GameDelta> handler)
        {
            lock (_gate) _subs.Add(new Sub(types, handler));
        }

        /// Publish one delta to every interested subscriber and record it in the recent log.
        public void Publish(GameDelta delta)
        {
            _log.Enqueue(delta);
            while (_log.Count > LogCapacity)
                _log.TryDequeue(out _);

            Sub[] subs;
            lock (_gate) subs = _subs.ToArray();

            foreach (var s in subs)
            {
                if (!s.Matches(delta.Type)) continue;
                try { s.Handler(delta); } catch { /* one subscriber must not break the pipeline */ }
            }
        }

        public IReadOnlyList<GameDelta> Recent() => _log.ToArray();

        public void Clear() => _log.Clear();

        // A null type-set means "match everything".
        private sealed record Sub(HashSet<GameDeltaType>? Types, Action<GameDelta> Handler)
        {
            public bool Matches(GameDeltaType t) => Types is null || Types.Contains(t);
        }
    }
}
