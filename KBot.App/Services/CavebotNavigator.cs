using KBot.App.Models;

namespace KBot.App.Services;

// Executes a route only while the native reader reports a reliable position.
// Every movement is a single step so STOP can cancel between commands.
public sealed class CavebotNavigator : IDisposable
{
    private const int TileTolerance = 1;
    private static readonly TimeSpan WaypointTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(120);
    private const int StallLimit = 8;

    private readonly NativeService _native;
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;

    public CavebotNavigator(NativeService native) => _native = native;

    public bool IsRunning { get; private set; }
    public string Status { get; private set; } = "Aguardando uma rota.";
    public int CurrentWaypointNumber { get; private set; }
    public int WaypointsCompleted { get; private set; }
    public int TotalWaypoints { get; private set; }
    public (int X, int Y, int Z)? LastPosition { get; private set; }

    public event Action? Changed;

    public async Task<bool> StartAsync(IReadOnlyList<CavebotWaypoint> route)
    {
        Stop();
        if (route.Count == 0)
        {
            Status = "Rota bloqueada: adicione ao menos um waypoint.";
            AutomationEventHub.Shared.Publish(AutomationEventSeverity.Warning, "Route", "route_blocked", Status);
            Raise();
            return false;
        }

        var status = await _native.GetStatusAsync(CancellationToken.None);
        if (status is not { NativeOnline: true, ClientFound: true, HasPosition: true, ReaderStatus: "READY" })
        {
            Status = $"Rota bloqueada pela fonte de posição ({status?.ReaderStatus ?? "sem resposta"}).";
            AutomationEventHub.Shared.Publish(AutomationEventSeverity.Warning, "Route", "position_unavailable", Status);
            Raise();
            return false;
        }

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        IsRunning = true;
        TotalWaypoints = route.Count;
        WaypointsCompleted = 0;
        CurrentWaypointNumber = 0;
        AutomationEventHub.Shared.Publish(AutomationEventSeverity.Info, "Route", "route_started",
            $"Rota iniciada com {route.Count} waypoint(s).", status.Pid.ToString());
        Raise();
        _runTask = RunAsync(route, token);
        await _runTask;
        return WaypointsCompleted == route.Count;
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public Task CompleteAsync() => _runTask ?? Task.CompletedTask;
    public void Dispose() => Stop();
    private void Raise() => Changed?.Invoke();

    private async Task RunAsync(IReadOnlyList<CavebotWaypoint> route, CancellationToken cancellationToken)
    {
        try
        {
            for (var index = 0; index < route.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var waypoint = route[index];
                CurrentWaypointNumber = waypoint.Number;
                Status = $"Ponto {waypoint.Number}/{route.Count} ({waypoint.DisplayName}): caminhando até {waypoint.Position}.";
                Raise();

                if (!await WalkToAsync(waypoint.X, waypoint.Y, waypoint.Z, cancellationToken))
                {
                    Status = $"Ponto {waypoint.Number}/{route.Count}: não chegou ao alvo {waypoint.Position}.";
                    AutomationEventHub.Shared.Publish(AutomationEventSeverity.Warning, "Route", "waypoint_failed", Status,
                        waypoint.Number.ToString());
                    Raise();
                    break;
                }

                if (!await PerformActionAsync(waypoint, cancellationToken)) break;
                WaypointsCompleted++;
                Status = $"Ponto {waypoint.Number}/{route.Count} concluído.";
                AutomationEventHub.Shared.Publish(AutomationEventSeverity.Info, "Route", "waypoint_reached", Status,
                    waypoint.Number.ToString());
                Raise();
            }

            if (!cancellationToken.IsCancellationRequested && WaypointsCompleted == route.Count)
                Status = $"Rota concluída: {route.Count} pontos. Personagem parado.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = $"Rota parada pelo usuário. {WaypointsCompleted}/{TotalWaypoints} pontos concluídos.";
            AutomationEventHub.Shared.Publish(AutomationEventSeverity.Info, "Route", "route_stopped", Status);
        }
        finally
        {
            IsRunning = false;
            Raise();
        }
    }

    private async Task<bool> WalkToAsync(int targetX, int targetY, int targetZ, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var lastSeen = (X: 0, Y: 0);
        var hasLastSeen = false;
        var stallReads = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (DateTime.UtcNow - startedAt > WaypointTimeout)
            {
                Status = "Rota suspensa: timeout no waypoint.";
                return false;
            }

            var status = await _native.GetStatusAsync(cancellationToken);
            if (status is not { NativeOnline: true, ClientFound: true, HasPosition: true, ReaderStatus: "READY" })
            {
                Status = $"Rota suspensa: posição indisponível ({status?.ReaderStatus ?? "sem resposta"}).";
                AutomationEventHub.Shared.Publish(AutomationEventSeverity.Warning, "Route", "position_lost", Status);
                Raise();
                return false;
            }

            LastPosition = (status.PosX, status.PosY, status.PosZ);
            if (status.PosZ != targetZ)
            {
                Status = $"Rota suspensa: waypoint no andar {targetZ}, posição atual no andar {status.PosZ}.";
                AutomationEventHub.Shared.Publish(AutomationEventSeverity.Warning, "Route", "floor_transition_required", Status);
                Raise();
                return false;
            }

            var deltaX = targetX - status.PosX;
            var deltaY = targetY - status.PosY;
            if (Math.Abs(deltaX) <= TileTolerance && Math.Abs(deltaY) <= TileTolerance) return true;

            var key = Math.Abs(deltaX) >= Math.Abs(deltaY)
                ? (deltaX > 0 ? "D" : "A")
                : (deltaY > 0 ? "S" : "W");

            if (hasLastSeen && lastSeen.X == status.PosX && lastSeen.Y == status.PosY)
            {
                if (++stallReads >= StallLimit)
                {
                    Status = "Rota suspensa: cliente parou de se mover.";
                    return false;
                }
            }
            else
            {
                stallReads = 0;
                lastSeen = (status.PosX, status.PosY);
                hasLastSeen = true;
            }

            await _native.SendKeyAsync(key, cancellationToken);
            await Task.Delay(StepDelay, cancellationToken);
        }
        return false;
    }

    private async Task<bool> PerformActionAsync(CavebotWaypoint waypoint, CancellationToken cancellationToken)
    {
        if (waypoint.Action == WaypointAction.Walk)
        {
            await Task.Delay(150, cancellationToken);
            return true;
        }
        if (waypoint.Action == WaypointAction.Wait)
        {
            await Task.Delay(waypoint.DelayMs > 0 ? waypoint.DelayMs : 300, cancellationToken);
            return true;
        }

        Status = $"Ação '{waypoint.Action}' bloqueada: falta integração confirmada para '{waypoint.Argument}'.";
        AutomationEventHub.Shared.Publish(AutomationEventSeverity.Warning, "Route", "action_blocked", Status,
            waypoint.Number.ToString());
        Raise();
        return false;
    }
}
