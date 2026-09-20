using KBot.App.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KBot.App.Services;

// Decides movement and drives the route. Reads the live position from the native
// core via GET_STATUS and pushes single directions with SEND_KEY. All memory reads
// stay on the C++ side; this class never touches process memory.
public sealed class CavebotNavigator
{
    private const int TileTolerance = 1;
    private static readonly TimeSpan WaypointTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(120);
    private const int StallLimit = 8;

    private readonly NativeService _native;
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;

    public CavebotNavigator(NativeService native)
    {
        _native = native;
    }

    public bool IsRunning { get; private set; }
    public string Status { get; private set; } = "Aguardando uma rota.";
    public int CurrentWaypointNumber { get; private set; } = 0;
    public int WaypointsCompleted { get; private set; } = 0;
    public int TotalWaypoints { get; private set; } = 0;
    public (int X, int Y, int Z)? LastPosition { get; private set; }

    public event Action? Changed;

    public async Task<bool> StartAsync(IReadOnlyList<CavebotWaypoint> route)
    {
        if (route.Count == 0)
        {
            Status = "A rota está vazia; adicione pelo menos um waypoint.";
            return false;
        }
        Stop();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        IsRunning = true;
        TotalWaypoints = route.Count;
        WaypointsCompleted = 0;
        Raise();
        _runTask = RunAsync(route, token);
        await _runTask;
        return true;
    }

    public void Stop()
    {
        if (_cancellation is not null)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    public Task CompleteAsync()
    {
        return _runTask ?? Task.CompletedTask;
    }

    public void Dispose()
    {
        Stop();
    }

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

                var reached = await WalkToAsync(waypoint.X, waypoint.Y, cancellationToken);
                if (!reached)
                {
                    Status = $"Ponto {waypoint.Number}/{route.Count}: não chegou ao alvo {waypoint.Position} (timeout/stall).";
                    Raise();
                    break;
                }

                if (!await PerformActionAsync(waypoint.Action, cancellationToken)) break;
                WaypointsCompleted++;
                Status = $"Ponto {waypoint.Number}/{route.Count} concluído.";
                Raise();
            }

            if (!cancellationToken.IsCancellationRequested && WaypointsCompleted == route.Count)
                Status = $"Rota concluída: {route.Count} pontos. Personagem parado.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = $"Rota parada pelo usuário. {WaypointsCompleted}/{TotalWaypoints} pontos concluídos.";
        }
        finally
        {
            IsRunning = false;
            Raise();
        }
    }

    private async Task<bool> WalkToAsync(int targetX, int targetY, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var lastSeen = (0, 0, DateTime.MinValue);
        var stallReads = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (DateTime.UtcNow - startedAt > WaypointTimeout)
            {
                Status += " Timeout no waypoint.";
                return false;
            }

            var status = await _native.GetStatusAsync(cancellationToken);
            if (status is null || !status.HasPosition)
            {
                Status = $"Lendo posição do cliente ({(status?.ReaderStatus ?? "sem resposta")})...";
                Raise();
                await Task.Delay(250, cancellationToken);
                continue;
            }

            LastPosition = (status.PosX, status.PosY, status.PosZ);
            var deltaX = targetX - status.PosX;
            var deltaY = targetY - status.PosY;

            if (Math.Abs(deltaX) <= TileTolerance && Math.Abs(deltaY) <= TileTolerance)
                return true;

            // Dominant-axis step keeps the path smooth and matches the grid feel.
            var key = Math.Abs(deltaX) >= Math.Abs(deltaY)
                ? (deltaX > 0 ? "D" : "A")
                : (deltaY > 0 ? "S" : "W");

            if (lastSeen.Item1 == status.PosX && lastSeen.Item2 == status.PosY)
            {
                stallReads++;
                if (stallReads >= StallLimit)
                {
                    Status += " Cliente parou de se mover.";
                    return false;
                }
            }
            else
            {
                stallReads = 0;
                lastSeen = (status.PosX, status.PosY, DateTime.UtcNow);
            }

            await _native.SendKeyAsync(key, cancellationToken);
            await Task.Delay(StepDelay, cancellationToken);
        }
        return false;
    }

    private async Task<bool> PerformActionAsync(WaypointAction action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case WaypointAction.Walk:
            case WaypointAction.Wait:
                await Task.Delay(action == WaypointAction.Wait ? 300 : 150, cancellationToken);
                return true;
            case WaypointAction.Talk:
            case WaypointAction.Use:
            case WaypointAction.StartAttacker:
            case WaypointAction.StopAttacker:
            case WaypointAction.OrderPokemon:
                // Pending: these actions need their own confirmed hotkeys/offsets.
                Status = $"Ação '{action}' pendente: falta atalho/offset confirmado para executar.";
                Raise();
                await Task.Delay(150, cancellationToken);
                return true;
            default:
                return true;
        }
    }
}
