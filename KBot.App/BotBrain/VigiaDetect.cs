namespace KBot.App.BotBrain;

// ============================================================================
//  VIGIA / PUXAO  (port of nF8_vigia.lua - "O PUXAO")
// ---------------------------------------------------------------------------
//  Pure state machine behind the "pulled" alarm. Being TELEPORTED without the
//  bot asking is the one signal with no innocent version: a GM dragged the
//  character. The hard part is NOT detecting the jump - it is NOT shouting on
//  legitimate teleports. A jump only alarms when nobody claims it:
//    * the bot itself stamps the teleports IT caused (Esperado / EsperadoDe);
//    * death stamps itself through the server chat phrase (module stamps it);
//    * the first 10 seconds after (re)entering the game never count;
//    * the player can TEACH a point (EnsinarDe/EnsinarPra) - a route teleport
//      or a hunt exit spot stops nagging after one time.
//
//  THE COUNT: a normal step moves 1 tile; stairs/pit move 1 tile changing 1
//  floor. Beyond that is a jump, and its size decides. viaScan=true means the
//  caller compared two photos (our ~1s scan): walking can appear as several
//  tiles at once if the client stuttered, so a scan-path jump also needs to be
//  larger than what you could WALK in the elapsed time (~90ms per tile), capped
//  at 8 tiles (above that it is a teleport no matter how long it took - without
//  the cap one long stall would swallow a 100-tile pull silently).
// ============================================================================
public sealed record PullInfo(int FromX, int FromY, int FromZ, int ToX, int ToY, int ToZ, int Dist, int Dz)
{
    public string Describe() =>
        $"PUXARAM VOCE: de {FromX},{FromY},{FromZ} para {ToX},{ToY},{ToZ} " +
        $"({Dist} tiles{(Dz > 0 ? $", {Dz} andar(es)" : string.Empty)}). O bot foi PARADO.";
}

public sealed class VigiaTracker
{
    public const long SettleMs = 10_000;       // grace right after entering the game
    public const long StepMsPerTile = 90;      // fastest walking in the game
    public const int ScanCeilingTiles = 8;     // above this a scan jump is always a teleport

    // Tiles of a jump that count as a pull (Lua default 3 - GMs pull NEAR, 3-4 tiles).
    public int DistThreshold { get; set; } = 3;

    // Latched while the alarm is ringing; releases on Silenciar() so the same
    // pull never re-alarms and the next pull needs a fresh jump.
    public bool Alarmed { get; private set; }
    public string? LastReason { get; private set; }  // why the last jump did NOT alarm

    private PokePos? _ult;
    private long _ultAt;
    private long _onlineAt = -1;
    private long _okAte;
    private string? _okPor;
    private (int X, int Y, int Z, long Ate, int Raio)? _origem;
    private readonly HashSet<string> _tpDe = new();
    private readonly HashSet<string> _tpPra = new();

    // Dropped out of the game: forget the last photo and take a fresh 10s grace
    // when coming back; a pull alarm that was ringing dies with the connection.
    public void Offline(long nowMs)
    {
        _ult = null;
        _ultAt = nowMs;
        _onlineAt = nowMs;
        Alarmed = false;
    }

    // "that teleport was me" - short stamp; EXTENDS the quiet window, never shrinks.
    public void Esperado(long nowMs, long ms, string motivo)
    {
        if (ms < 1000) ms = 1000;
        if (ms > 60_000) ms = 60_000;
        _okAte = System.Math.Max(_okAte, nowMs + ms);
        _okPor = motivo;
    }

    // The LATE teleport (the Auto Hunt door): wider window, but it only excuses a
    // jump LEAVING near the stamped tile (same floor, within raio) - a pull from
    // anywhere else still alarms. Consumed on use.
    public void EsperadoDe(long nowMs, int x, int y, int z, long ms, string motivo, int raio = 3)
    {
        if (ms > 180_000) ms = 180_000;
        _origem = (x, y, z, nowMs + ms, raio);
    }

    public void EnsinarDe(int x, int y, int z) => _tpDe.Add($"{x},{y},{z}");
    public void EnsinarPra(int x, int y, int z) => _tpPra.Add($"{x},{y},{z}");
    // The window button: one click teaches BOTH ends of that alarm - the route
    // teleport is recognised by its ORIGIN, the hunt exit by its DESTINATION.
    // Without both, one of the two cases would nag forever.
    public void EnsinarPontos(int x, int y, int z) { _tpDe.Add($"{x},{y},{z}"); _tpPra.Add($"{x},{y},{z}"); }
    public void EsquecerPontos() { _tpDe.Clear(); _tpPra.Clear(); }
    public void Silenciar() => Alarmed = false;

    // Feed the NEW position every tick; returns the pull that alarmed (or null).
    public PullInfo? Mede(long nowMs, PokePos? novo, bool viaScan, bool dead)
    {
        if (novo is null) return null;
        if (_onlineAt < 0) _onlineAt = nowMs;
        var velho = _ult;
        long velhoAt = _ultAt;
        _ult = novo.Value;
        _ultAt = nowMs;
        if (velho is null) return null;                       // first photo
        if (velho == novo.Value) return null;                 // same tile

        int dist = System.Math.Max(System.Math.Abs(novo.Value.X - velho.Value.X),
                                   System.Math.Abs(novo.Value.Y - velho.Value.Y));
        int dz = System.Math.Abs(novo.Value.Z - velho.Value.Z);
        if (dist <= 1 && dz <= 1) return null;                // step, stair, pit

        int limiar = System.Math.Max(2, DistThreshold);
        bool salto = dist >= limiar || dz >= 2 || (dz >= 1 && dist >= 3);
        if (!salto) return null;

        if (viaScan && dz < 2)
        {
            // Two photos, not one event: walking can span several tiles if the
            // client stuttered. Cap the "could have walked" range at 8 tiles.
            long elapsed = nowMs - velhoAt;
            if (elapsed < 0) elapsed = 0;
            int andavel = System.Math.Min(ScanCeilingTiles, (int)(elapsed / StepMsPerTile) + 1);
            if (dist <= andavel) return null;
        }

        if (Alarmed) return null;                              // already screaming

        LastReason = null;
        if (nowMs - _onlineAt < SettleMs) { LastReason = "10 primeiros segundos de jogo"; return null; }
        if (nowMs < _okAte) { LastReason = $"salto esperado ({_okPor})"; return null; }
        if (_origem is { } o && nowMs < o.Ate && velho.Value.Z == o.Z
            && System.Math.Max(System.Math.Abs(velho.Value.X - o.X), System.Math.Abs(velho.Value.Y - o.Y)) <= o.Raio)
        {
            _origem = null;                                    // vale UMA vez
            LastReason = "saindo de onde o bot pediu";
            return null;
        }
        if (dead) { LastReason = "personagem morto (volta pro templo)"; return null; }
        if (_tpDe.Contains($"{velho.Value.X},{velho.Value.Y},{velho.Value.Z}"))
        { LastReason = "ponto ensinado (de)"; return null; }
        if (_tpPra.Contains($"{novo.Value.X},{novo.Value.Y},{novo.Value.Z}"))
        { LastReason = "ponto ensinado (pra)"; return null; }

        Alarmed = true;
        return new PullInfo(velho.Value.X, velho.Value.Y, velho.Value.Z,
                            novo.Value.X, novo.Value.Y, novo.Value.Z, dist, dz);
    }
}
