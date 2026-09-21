using System;
using System.Collections.Generic;
using System.Linq;

namespace KBot.App.Services;

public enum AutomationEventSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public sealed record AutomationEvent(
    DateTimeOffset Timestamp,
    AutomationEventSeverity Severity,
    string Source,
    string EventCode,
    string Message,
    string ContextId = "")
{
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string SeverityText => Severity.ToString().ToUpperInvariant();
    public string Title => EventCode.Replace('_', ' ');
    public bool Acknowledged { get; init; }
}

// Central, bounded event stream shared by session, automation modules and UI.
// Repeated events are rate limited by source/code/context so a one-second poll
// cannot flood the operator log with the same state.
public sealed class AutomationEventHub
{
    private const int Capacity = 250;
    private static readonly TimeSpan DefaultDedupeWindow = TimeSpan.FromSeconds(4);
    private readonly object _gate = new();
    private readonly List<AutomationEvent> _events = new();
    private readonly Dictionary<string, DateTimeOffset> _lastByKey = new(StringComparer.Ordinal);

    public static AutomationEventHub Shared { get; } = new();

    public event Action<AutomationEvent>? Published;
    public event Action? Cleared;

    public bool Publish(AutomationEventSeverity severity, string source, string eventCode,
        string message, string contextId = "", TimeSpan? dedupeWindow = null)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(eventCode) ||
            string.IsNullOrWhiteSpace(message)) return false;

        var now = DateTimeOffset.Now;
        var key = $"{source.Trim()}\u001f{eventCode.Trim()}\u001f{contextId.Trim()}\u001f{message.Trim()}";
        var window = dedupeWindow ?? DefaultDedupeWindow;
        AutomationEvent item;
        lock (_gate)
        {
            if (_lastByKey.TryGetValue(key, out var last) && now - last < window) return false;
            _lastByKey[key] = now;
            item = new AutomationEvent(now, severity, source.Trim(), eventCode.Trim(), message.Trim(), contextId.Trim());
            _events.Add(item);
            if (_events.Count > Capacity) _events.RemoveRange(0, _events.Count - Capacity);
        }
        if (Published is { } published)
            foreach (Action<AutomationEvent> handler in published.GetInvocationList())
                try { handler(item); } catch { }
        return true;
    }

    public IReadOnlyList<AutomationEvent> Snapshot()
    {
        lock (_gate) return _events.ToList();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
            _lastByKey.Clear();
        }
        if (Cleared is { } cleared)
            foreach (Action handler in cleared.GetInvocationList())
                try { handler(); } catch { }
    }
}
