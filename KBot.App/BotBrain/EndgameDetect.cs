namespace KBot.App.BotBrain;

// ============================================================================
//  END GAME / AUTO COMBO  (port of main_endgame.lua - "SOFA // Auto Combo")
// ---------------------------------------------------------------------------
//  Rotation of TWO teams of three (1 tank + 2 area-damage) that farms big
//  waves WITHOUT revive: the swap itself is the cooldown reset. One full
//  cycle:
//    1) team-1 TANK goes out (potion if it came back hurt) and walks, pulling
//       the wave - nobody attacks in this phase;
//    2) the character STOPS when the whole wave is on screen, or when it is
//       glued to one of them;
//    3) wait until ALL of them close IN AROUND THE TANK (ring tiles) - strict
//       junção, the tank never combos an incomplete wave;
//    4) the character WALKS to the pile: a swapped poke spawns BESIDE THE
//       CHARACTER, so both damage pokes must be born inside the wave;
//    5) tank FULL COMBO (it is the one that stuns the wave);
//    6) damage 1 -> full combo, damage 2 -> idem (each only swaps with an
//       EMPTY kit: it recasts until everything that was ready got cast);
//    7) swap to the TEAM-2 TANK, which keeps walking for the next wave while
//       team 1 rests in its balls; loops forever.
//
//  HARD RULES (from the design doc):
//    * NEVER revive - a fainted poke is SKIPPED;
//    * NEVER guess a slot - a poke is found by NAME in the pokebar and an
//      ambiguous name ABORTS the role instead of risking the wrong swap;
//    * NEVER combo under the configured count - if the wave stalls, walk again
//      to pull more instead of comboing incomplete;
//    * tank is never left to die: potion on the way back, rescue potion mid-
//      junção, and below the swap line the swap is pulled forward early (a
//      swap collects the tank = pulls it out of the middle of the wave).
//
//  Pure state machine: this file has NO memory/vision reads. The module feeds
//  it one snapshot per tick (pokebar, active role, active hp, wild counts
//  pre-computed from the scan, nearest wild, kit-ready count, safe spot) and
//  consumes intent strings; translating those into ActionIntents belongs to
//  the module. Offset-pending pieces degrade like everywhere else:
//    * no pokebar            -> silent arm, nothing is stamped;
//    * kit/cd unreadable     -> combo length defaults to 12 (Lua: all slots);
//                               in-ball team cd unreadable -> counts as READY
//                               (the recoverMax ceiling still bounds it);
//    * poke position unread  -> the cluster is measured from the CHARACTER tile
//                               and the walk-to-pile collapses to "combo in
//                               place" (dist = 0);
//    * safe spot unset (0,0,0) -> recover in place.
// ============================================================================

// One of the six fixed rotation seats. Names come from the profile; an EMPTY
// name simply skips the seat, so a half-configured profile degrades to the
// seats that ARE named.
public sealed record EndgameRole(string Name, string Key, int Team, bool IsTank)
{
    public string Label => $"Time {Team} / {(IsTank ? "Tank" : Key == "d1" ? "Dano 1" : "Dano 2")}";
}

// Everything the tracker needs from the profile each tick. Built fresh by the
// module so live edits apply immediately. PotItem below 100 disables potions.
public sealed record EndgameConfig(
    IReadOnlyList<EndgameRole> Roles,          // fixed 6: T1 tank,d1,d2, T2 tank,d1,d2
    int WaveCount, int RingTiles, int SeeStop, int StopDist,
    int ApproachSqm, int RelureS, int MoveGapMs,
    int PotItem, int PotPct, int SavePct, int SwapPct,
    bool UseSafe, int SafeReach, int RecoverMaxS, int Reburst,
    bool PokeStop)
{
    public static EndgameRole[] FixedRoles(
        string t1Tank, string t1d1, string t1d2,
        string t2Tank, string t2d1, string t2d2) => new[]
        {
            new EndgameRole(t1Tank, "tank", 1, true),
            new EndgameRole(t1d1,   "d1",   1, false),
            new EndgameRole(t1d2,   "d2",   1, false),
            new EndgameRole(t2Tank, "tank", 2, true),
            new EndgameRole(t2d1,   "d1",   2, false),
            new EndgameRole(t2d2,   "d2",   2, false),
        };
}

// The single tick input. Wild questions are pre-computed by the module from
// the screen scan (same floor, chebyshev):
//   StuckWildCount   - alive wilds within RingTiles of US;
//   ScreenWildCount  - alive wilds within 7 tiles of US (the "on screen" ring);
//   NearestWildDist  - closest alive wild, -1 when none;
//   NearestWildName  - that wild's name (single-target aim); null = none.
// ActiveRole is "" when we cannot tell which poke is on the field; ActiveHp
// null when unread. TeamCdReady null = "can't read the in-ball cooldown" ->
// the team counts as READY (Lua egTeamReady: checked==0 -> true).
// PokeX/PokeY null = the poke's tile is unknown -> approach treats dist as 0.
public readonly record struct EndgameSnapshot(
    long NowMs, bool Online,
    int CharX, int CharY, int CharZ,
    string ActiveRole, double? ActiveHp,
    int StuckWildCount, int ScreenWildCount,
    int NearestWildDist, string? NearestWildName,
    int KitReadyMoves, bool? TeamCdReady,
    int? PokeX, int? PokeY,
    int SafeX, int SafeY, int SafeZ,
    IReadOnlyList<PokebarSlot> Pokebar);

public sealed record EndgameStep(string Intent, string Arg, string RoleLabel, string Detail);

public static class EndgameIntents
{
    public const string Hold = "";
    public const string Summon = "summon";     // arg "name:slot"
    public const string Potion = "potion";     // arg "itemId"
    public const string Cast = "cast";         // arg "tank|dmg:count[:aim:name]"
    public const string PokeStop = "pokestop";
    public const string MovePile = "movepile"; // arg "W/A/S/D"
    public const string MoveSafe = "movesafe"; // arg "W/A/S/D"
}

public sealed class EndgameTracker
{
    // Phase constants (the Lua phase names, kept verbatim for traceability).
    public const string Send = "send";
    public const string Out = "out";
    public const string Pot = "pot";
    public const string Dmg = "dmg";
    public const string Lure = "lure";
    public const string Gather = "gather";
    public const string Approach = "approach";
    public const string Burst = "burst";
    public const string BurstWait = "burstwait";
    public const string GoSafe = "gosafe";
    public const string Recover = "recover";

    public const int SendTriesCap = 4;          // failed sends per role before skipping
    public const int ApproachStepCap = 8;       // steps toward the pile before combo-in-place
    public const int ApproachTimeCapMs = 4000;
    public const int SafeStepCap = 40;          // steps toward the safe spot before recovering in place
    public const int ScreenRadius = 7;          // the "on screen" wilds ring (Lua hardcode)
    public const long OutConfirmMs = 450;       // cdBar cache grace after a swap
    public const long SwapGraceMs = 1500;       // tank-swap rescue cannot fire right after the swap-out
    public const long PotionItemCdMs = 10_000;  // the server-side potion cooldown
    public const long RelureWalkMs = 6000;      // forced walking window after a re-lure decision
    public const long StaleResetMs = 2000;      // macro off -> forget all cycle state
    public const int DefaultKitMoves = 12;      // kit unreadable -> Lua casts all 12 slots
    public const long StepGateMs = 250;         // per-step pace (Lua egPing(250))

    public string Phase { get; private set; } = "";
    public int RoleIndex { get; private set; } = 0;   // 0..5 in the fixed rotation
    public string LastDetail { get; private set; } = "";

    private long _at;                 // per-phase gate ("not earlier than")
    private long _gatherSince;        // since when we have been standing waiting for the wave to close
    private long _apprSince;
    private int _apprSteps;
    private long _relureUntil;
    private long _outAt;              // since when the CURRENT role's poke has been out
    private int _reburst;
    private long _burstEnds;
    private long _recoverSince;
    private long _potAt;
    private long _safeAt;
    private int _safeSteps;
    private long _lastTick = -1;
    private bool _pokestopSent;       // !pokestop was already spoken for the current out poke

    public void Reset()
    {
        Phase = "";
        _at = 0;
        _gatherSince = 0;
        _apprSince = 0;
        _apprSteps = 0;
        _relureUntil = 0;
        _outAt = 0;
        _reburst = 0;
        _burstEnds = 0;
        _recoverSince = 0;
        _potAt = 0;
        _safeAt = 0;
        _safeSteps = 0;
        _pokestopSent = false;
    }

    // ------------------------------------------------------------------ roles
    // Name -> slot, the GOLDEN RULE: never guess. Empty bar / empty name /
    // zero hits -> null (falls back to the prefix-stripped second chance).
    // Two+ hits -> ambiguous -> null (the role is skipped, never resolved to
    // the wrong poke).
    public static int? ResolveSlot(string name, IReadOnlyList<PokebarSlot> pokebar)
    {
        var want = Norm(name);
        if (want.Length == 0 || pokebar.Count == 0) return null;
        int hits = 0, slot = 0;
        for (int i = 0; i < pokebar.Count; i++)
            if (Norm(pokebar[i].Name).Equals(want, StringComparison.Ordinal)) { hits++; slot = i + 1; }
        if (hits == 0)
        {
            var wnp = NormNP(name);
            hits = 0; slot = 0;
            for (int i = 0; i < pokebar.Count; i++)
                if (NormNP(pokebar[i].Name).Equals(wnp, StringComparison.Ordinal)) { hits++; slot = i + 1; }
        }
        return hits == 1 ? slot : null;
    }

    // Is the poke THIS role asks for the one currently out? Confirms a swap
    // was accepted by the server (name match, second chance without prefixes).
    internal static bool MatchesOutRole(string wantName, string? outName)
    {
        if (string.IsNullOrEmpty(outName)) return false;
        if (Norm(wantName).Equals(Norm(outName), StringComparison.Ordinal)) return true;
        return NormNP(wantName).Equals(NormNP(outName), StringComparison.Ordinal);
    }

    // Chebyshev step toward a point; dominant axis wins (matches the cavebot).
    public static string StepDirection(int fx, int fy, int tx, int ty)
    {
        int dx = tx - fx, dy = ty - fy;
        if (System.Math.Abs(dx) >= System.Math.Abs(dy))
            return dx > 0 ? "D" : dx < 0 ? "A" : "idle";
        return dy > 0 ? "S" : dy < 0 ? "W" : "idle";
    }

    // ------------------------------------------------------------------- main
    public EndgameStep Tick(EndgameConfig c, EndgameSnapshot s)
    {
        if (_lastTick > 0 && s.NowMs - _lastTick > StaleResetMs) Reset();  // macro was off
        _lastTick = s.NowMs;

        if (!s.Online || s.Pokebar.Count == 0)
        {
            Reset();
            var msg = s.Online ? "esperando a barra de pokemons" : "offline";
            return Step(EndgameIntents.Hold, "", msg);
        }

        if (Phase.Length == 0 && !Goto(RoleIndex, c, s))
            return Step(EndgameIntents.Hold, "", "nenhum pokemon utilizavel");

        var r = c.Roles[RoleIndex];
        if (string.IsNullOrWhiteSpace(r.Name))
        {
            // Name edited out mid-cycle: hop to the next usable seat and
            // REBIND the role before dispatching, or we'd run the old seat.
            if (!Advance(c, s)) return Step(EndgameIntents.Hold, "", "nenhum pokemon utilizavel");
            r = c.Roles[RoleIndex];
        }

        switch (Phase)
        {
            case Send: return DoSend(c, s, r);
            case Out: return DoOut(c, s, r);
            case Pot: return DoPot(c, s);
            case Dmg: return DoDmg(c, s, r);
            case Lure: return DoLure(c, s, r);
            case Gather: return DoGather(c, s, r);
            case Approach: return DoApproach(c, s, r);
            case Burst: return DoBurst(c, s, r);
            case BurstWait: return DoBurstWait(c, s, r);
            case GoSafe: return DoGoSafe(c, s, r);
            case Recover: return DoRecover(c, s, r);
            default:
                Reset();
                return Step(EndgameIntents.Hold, "", "fase desconhecida");
        }
    }

    private static EndgameStep Step(string intent, string arg, string detail) =>
        new(intent, arg, "", detail);

    private static EndgameStep RoleStep(EndgameRole r, string intent, string arg, string detail) =>
        new(intent, arg, r.Label, detail);

    // ------------------------------------------------------------- role hops
    // Jump to seat `i` (or the next usable seat from there). Returns false when
    // NONE of the six seats is usable (the caller reports / disarms).
    private bool Goto(int i, EndgameConfig c, EndgameSnapshot s)
    {
        for (int k = 0; k < c.Roles.Count; k++)
        {
            int j = (i + k) % c.Roles.Count;
            if (!Usable(c, s, j)) continue;
            RoleIndex = j;
            Phase = Send;
            _at = s.NowMs;
            _gatherSince = 0;
            _apprSince = 0;
            _apprSteps = 0;
            _outAt = 0;
            _reburst = 0;
            _tries = 0;
            _pokestopSent = false;
            return true;
        }
        Reset();
        return false;
    }

    private bool Advance(EndgameConfig c, EndgameSnapshot s) =>
        Goto((RoleIndex % c.Roles.Count) + 1, c, s);

    // Wave cleared -> next TEAM, through the safe spot when configured.
    private bool JumpNextTeam(EndgameConfig c, EndgameSnapshot s)
    {
        int team = c.Roles[RoleIndex].Team;
        int otherTank = team == 1 ? 3 : 0;   // 0-based: T2 tank / T1 tank
        if (!c.UseSafe) return Goto(otherTank, c, s);
        _reburst = 0;
        _at = s.NowMs;
        _outAt = 0;
        _pokestopSent = false;
        if (!Usable(c, s, otherTank)) return Goto(otherTank, c, s);
        RoleIndex = otherTank;
        _safeSteps = 0;
        _safeAt = s.NowMs;
        _recoverSince = 0;
        Phase = (s.SafeX <= 0 && s.SafeY <= 0 && s.SafeZ <= 0) ? Recover : GoSafe;
        return true;
    }

    private static bool Usable(EndgameConfig c, EndgameSnapshot s, int i)
    {
        var r = c.Roles[i];
        if (string.IsNullOrWhiteSpace(r.Name)) return false;
        int? slot = ResolveSlot(r.Name, s.Pokebar);
        if (slot is null) return false;
        double? hp = s.Pokebar[slot.Value - 1].HealthPercent;
        return hp is null or > 0;
    }

    private static bool IsOut(EndgameSnapshot s, string name) =>
        MatchesOutRole(name, s.ActiveRole) && (s.ActiveHp is null or > 0);

    // Tank safety. allowSwap=false MID-BURST: the swap would collect the tank
    // while moves are still scheduled and burn the combo - only the potion acts.
    // Returns true (and a step) when it acted; the caller must return that step.
    private bool TankCare(EndgameConfig c, EndgameSnapshot s, EndgameRole r,
                          bool allowSwap, out EndgameStep step)
    {
        step = null!;
        if (!r.IsTank || s.ActiveHp is not {} hp) return false;

        if (allowSwap && c.SwapPct > 0 && hp <= c.SwapPct &&
            (_outAt <= 0 || s.NowMs - _outAt > SwapGraceMs))
        {
            Advance(c, s);
            step = Step(EndgameIntents.Hold, "", $"tank com {(int)hp}% - recolhendo agora (troca) pra nao morrer");
            return true;
        }
        if (c.SavePct > 0 && c.PotItem >= 100 && hp <= c.SavePct &&
            s.NowMs - _potAt > PotionItemCdMs)
        {
            _potAt = s.NowMs;
            step = RoleStep(r, EndgameIntents.Potion, c.PotItem.ToString(), $"potion de socorro no tank ({(int)hp}%)");
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------ phases ----
    private EndgameStep DoSend(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        if (IsOut(s, r.Name))                    // already on the field: follow the flow
        {
            _outAt = s.NowMs;
            if (!r.IsTank && c.PokeStop && !_pokestopSent)
            {
                _pokestopSent = true;
                Phase = Dmg;
                _at = s.NowMs;
                return RoleStep(r, EndgameIntents.PokeStop, "", $"{r.Name} parado no lugar");
            }
            Phase = r.IsTank ? Pot : Dmg;
            _at = s.NowMs;
            return Step(EndgameIntents.Hold, "", $"{r.Name} ja esta fora");
        }
        _outAt = 0;
        if (s.NowMs < _at) return Step(EndgameIntents.Hold, "", "aguardando troca");
        int? slot = ResolveSlot(r.Name, s.Pokebar);
        if (slot is null)
        {
            // Wrong or repeated name: wait, do NOT guess.
            _at = s.NowMs + 3000;
            return Step(EndgameIntents.Hold, "", $"nome invalido: {r.Name} (nao achei na pokebar)");
        }
        double? hp = s.Pokebar[slot.Value - 1].HealthPercent;
        if (hp is not null && hp <= 0)
        {
            if (!Advance(c, s)) return Step(EndgameIntents.Hold, "", "nenhum pokemon utilizavel");
            return Step(EndgameIntents.Hold, "", $"'{r.Name}' desmaiado - pulando (sem revive)");
        }
        if (++_tries > SendTriesCap)
        {
            if (!Advance(c, s)) return Step(EndgameIntents.Hold, "", "nenhum pokemon utilizavel");
            return Step(EndgameIntents.Hold, "", $"'{r.Name}' nao soltou - pulando");
        }
        _at = s.NowMs + 600;
        Phase = Out;
        return RoleStep(r, EndgameIntents.Summon, $"{r.Name}:{slot.Value}", $"soltando {r.Name} ({r.Label})");
    }

    private int _tries;

    private EndgameStep DoOut(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        if (IsOut(s, r.Name))
        {
            _outAt = s.NowMs;
            if (!r.IsTank && c.PokeStop && !_pokestopSent)
            {
                _pokestopSent = true;
                Phase = Dmg;
                _at = s.NowMs + OutConfirmMs;
                return RoleStep(r, EndgameIntents.PokeStop, "", $"{r.Name} parado no lugar");
            }
            Phase = r.IsTank ? Pot : Dmg;
            // cdBar cache: the move list may still be the PREVIOUS poke's for
            // ~450ms after the swap - don't let a damage poke combo on stale data.
            _at = s.NowMs + OutConfirmMs;
            return Step(EndgameIntents.Hold, "", $"'{r.Name}' fora ({r.Label})");
        }
        if (s.NowMs < _at) return Step(EndgameIntents.Hold, "", "aguardando confirmacao da troca");
        if (++_tries > SendTriesCap)
        {
            if (!Advance(c, s)) return Step(EndgameIntents.Hold, "", "nenhum pokemon utilizavel");
            return Step(EndgameIntents.Hold, "", $"'{r.Name}' nao saiu - pulando");
        }
        Phase = Send;
        _at = s.NowMs + 300;
        return Step(EndgameIntents.Hold, "", "troca nao confirmada - tentando de novo");
    }

    // Potion on the tank's way back to the wave. The Lua POTS even when the HP
    // read is NIL (cautious default right after the swap).
    private EndgameStep DoPot(EndgameConfig c, EndgameSnapshot s)
    {
        var r = c.Roles[RoleIndex];
        if (c.PotItem >= 100 &&
            (s.ActiveHp is {} hp && hp <= c.PotPct || s.ActiveHp is null) &&
            s.NowMs - _potAt > PotionItemCdMs)
        {
            _potAt = s.NowMs;
            Phase = Lure;
            _at = s.NowMs + 400;
            return RoleStep(r, EndgameIntents.Potion, c.PotItem.ToString(), "potion no tank ao voltar");
        }
        Phase = Lure;
        _at = s.NowMs;
        return Step(EndgameIntents.Hold, "", "tank pronto pra puxar");
    }

    // A damage poke, confirmed out: combo IMMEDIATELY (there is no walking for
    // it - the pile was formed by the tank). If the wave is already gone before
    // its turn, don't waste the combo - jump straight to the other team (this
    // poke's cooldown stays stored for later).
    private EndgameStep DoDmg(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        if (s.NowMs < _at) return Step(EndgameIntents.Hold, "", "aguardando confirmacao da troca");
        if (s.ScreenWildCount == 0)
        {
            JumpNextTeam(c, s);
            return Step(EndgameIntents.Hold, "", "wave limpa antes do dano combar - indo pro proximo time");
        }
        Phase = Burst;
        return Step(EndgameIntents.Hold, "", $"{r.Name} na wave - full combo");
    }

    private EndgameStep DoLure(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        if (TankCare(c, s, r, allowSwap: true, out var step)) return step;

        int stuck = s.StuckWildCount, seen = s.ScreenWildCount;
        if (stuck >= c.WaveCount)
        {
            _gatherSince = s.NowMs;
            Phase = Gather;
            return Step(EndgameIntents.Hold, "", "wave colou toda andando - parado, confirmando");
        }
        if (s.NowMs < _relureUntil)
            return Step(EndgameIntents.Hold, "", $"puxando mais ({stuck}/{c.WaveCount} colados)");
        bool perto = s.NearestWildDist >= 0 && s.NearestWildDist <= System.Math.Max(1, c.StopDist);
        if (seen >= c.SeeStop || perto)
        {
            _gatherSince = s.NowMs;
            Phase = Gather;
            return Step(EndgameIntents.Hold, "", $"parou ({seen} na tela) - esperando {c.WaveCount} colarem no tank");
        }
        return Step(EndgameIntents.Hold, "", $"puxando a wave ({stuck}/{c.WaveCount} colados) - cavebot anda");
    }

    private EndgameStep DoGather(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        if (TankCare(c, s, r, allowSwap: true, out var step)) return step;

        if (s.StuckWildCount >= c.WaveCount)
        {
            _apprSince = s.NowMs;
            _apprSteps = 0;
            Phase = Approach;
            return Step(EndgameIntents.Hold, "", $"wave fechada ({s.StuckWildCount}/{c.WaveCount}) - chegando na pilha");
        }
        // Stalled (behind a rock, weak spawn): NEVER combo incomplete - walk
        // again to pull the rest. RelureS=0 disables (waits forever).
        if (c.RelureS > 0 && s.NowMs - _gatherSince >= (long)c.RelureS * 1000)
        {
            _gatherSince = 0;
            _relureUntil = s.NowMs + RelureWalkMs;
            Phase = Lure;
            _at = s.NowMs;
            return Step(EndgameIntents.Hold, "", $"so {s.StuckWildCount} de {c.WaveCount} colaram em {c.RelureS}s - voltando a andar");
        }
        return Step(EndgameIntents.Hold, "", $"juntando: {s.StuckWildCount}/{c.WaveCount} colados no tank");
    }

    private EndgameStep DoApproach(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        if (TankCare(c, s, r, allowSwap: true, out var step)) return step;

        // Only the TANK re-checks: by the time the damage pokes act the wave is
        // already gathered + stunned (they don't re-gate).
        if (r.IsTank && s.StuckWildCount < c.WaveCount)
        {
            _gatherSince = s.NowMs;
            Phase = Gather;
            return Step(EndgameIntents.Hold, "", "a wave abriu durante a aproximacao - volta a esperar juntar");
        }
        // The poke's tile is unread today -> the pile is treated as reached
        // (combo in place); with a real read this becomes the walk-to-pile.
        int dist = s.PokeX is {} px && s.PokeY is {} py
            ? Chebyshev(s.CharX, s.CharY, px, py) : 0;
        int want = System.Math.Max(1, c.ApproachSqm);
        if (dist <= want || _apprSteps >= ApproachStepCap || s.NowMs - _apprSince > ApproachTimeCapMs)
        {
            Phase = Burst;
            return Step(EndgameIntents.Hold, "", dist <= want ? "na pilha - full combo" : $"comba de onde esta ({dist} sqm)");
        }
        if (s.NowMs < _at) return Step(EndgameIntents.Hold, "", "chegando na pilha");
        var dir = StepDirection(s.CharX, s.CharY, s.PokeX!.Value, s.PokeY!.Value);
        _apprSteps++;
        _at = s.NowMs + StepGateMs;
        return RoleStep(r, EndgameIntents.MovePile, dir, $"chegando na pilha ({dist} sqm)");
    }

    private EndgameStep DoBurst(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        // Strict TANK gate: the FIRST tank burst only fires with the full count
        // glued NOW. If the wave opened, go back to waiting.
        if (r.IsTank && _reburst == 0 && s.StuckWildCount < c.WaveCount)
        {
            _gatherSince = s.NowMs;
            Phase = Gather;
            return Step(EndgameIntents.Hold, "", $"TANK: so {s.StuckWildCount}/{c.WaveCount} colados na hora do combo - espera juntar");
        }
        int kit = System.Math.Max(1, System.Math.Min(12,
            s.KitReadyMoves > 0 ? s.KitReadyMoves : DefaultKitMoves));
        int last = (kit - 1) * System.Math.Max(60, c.MoveGapMs);
        _burstEnds = s.NowMs + last + 400;
        var aim = s.NearestWildName is { Length: > 0 } n ? $":aim:{n}" : "";
        Phase = BurstWait;
        return RoleStep(r, EndgameIntents.Cast, $"{(r.IsTank ? "tank" : "dmg")}:{kit}{aim}",
                        $"{(r.IsTank ? "TANK" : "DANO")} full combo ({kit} moves)");
    }

    private EndgameStep DoBurstWait(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        if (r.IsTank && TankCare(c, s, r, allowSwap: false, out var step)) return step;

        if (s.NowMs < _burstEnds) return Step(EndgameIntents.Hold, "", "combo saindo...");
        if (s.ScreenWildCount == 0)
        {
            JumpNextTeam(c, s);
            return Step(EndgameIntents.Hold, "", "wave limpa - indo pro proximo time");
        }
        // KIT VAZIO (damage pokes only): recast while anything that was ready is
        // still ready - that is what keeps the area combo from going out
        // halfway. The tank does NOT recast: it stuns and swaps fast.
        if (!r.IsTank && s.KitReadyMoves > 0 && _reburst < c.Reburst)
        {
            _reburst++;
            Phase = Burst;
            return Step(EndgameIntents.Hold, "", $"{r.Name} ainda tem move pronto - re-combo {_reburst} (esvaziar o kit)");
        }
        _reburst = 0;
        if (!Advance(c, s)) return Step(EndgameIntents.Hold, "", "nenhum pokemon utilizavel");
        return Step(EndgameIntents.Hold, "", "");
    }

    private EndgameStep DoGoSafe(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        if (s.SafeZ != s.CharZ || (s.SafeX <= 0 && s.SafeY <= 0 && s.SafeZ <= 0))
        {
            Phase = Recover;
            _recoverSince = 0;
            return Step(EndgameIntents.Hold, "", "safe spot em outro andar - recuperando aqui mesmo");
        }
        int d = Chebyshev(s.CharX, s.CharY, s.SafeX, s.SafeY);
        int reach = System.Math.Max(0, c.SafeReach);
        if (d <= reach || _safeSteps >= SafeStepCap)
        {
            Phase = Recover;
            _recoverSince = 0;
            return Step(EndgameIntents.Hold, "", d <= reach ? "no safe spot - recuperando" : $"safe inalcancavel ({d} sqm) - recupera onde deu");
        }
        if (s.NowMs < _safeAt) return Step(EndgameIntents.Hold, "", $"indo pro safe spot ({d} sqm)");
        var dir = StepDirection(s.CharX, s.CharY, s.SafeX, s.SafeY);
        _safeSteps++;
        _safeAt = s.NowMs + StepGateMs;
        return RoleStep(r, EndgameIntents.MoveSafe, dir, $"indo pro safe spot ({d} sqm)");
    }

    private EndgameStep DoRecover(EndgameConfig c, EndgameSnapshot s, EndgameRole r)
    {
        if (_recoverSince == 0) { _recoverSince = s.NowMs; _at = s.NowMs; _tries = 0; }

        if (!IsOut(s, r.Name))
        {
            if (s.NowMs < _at) return Step(EndgameIntents.Hold, "", "recuperando: aguardando troca");
            int? slot = ResolveSlot(r.Name, s.Pokebar);
            if (slot is null)
            {
                Phase = Lure;
                _at = s.NowMs;
                return Step(EndgameIntents.Hold, "", $"nao achei '{r.Name}' pra recuperar - seguindo");
            }
            double? hp = s.Pokebar[slot.Value - 1].HealthPercent;
            if (hp is not null && hp <= 0)
            {
                if (!Advance(c, s)) return Step(EndgameIntents.Hold, "", "nenhum pokemon utilizavel");
                return Step(EndgameIntents.Hold, "", $"'{r.Name}' desmaiado - pulando (sem revive)");
            }
            if (++_tries > SendTriesCap)
            {
                _tries = 0;
                Phase = Lure;
                _at = s.NowMs;
                return Step(EndgameIntents.Hold, "", $"'{r.Name}' nao soltou no safe spot - seguindo");
            }
            _at = s.NowMs + 600;
            return RoleStep(r, EndgameIntents.Summon, $"{r.Name}:{slot.Value}", "recuperando: soltando o tank");
        }
        _tries = 0;
        if (_outAt == 0) _outAt = s.NowMs;

        if (c.PotItem >= 100 && s.ActiveHp is double ahp && ahp <= c.PotPct &&
            s.NowMs - _potAt > PotionItemCdMs)
        {
            _potAt = s.NowMs;
            return RoleStep(r, EndgameIntents.Potion, c.PotItem.ToString(), $"recover: potion no tank ({(int)ahp}%)");
        }

        // In-ball "ready/total" is not readable yet -> the team counts as
        // READY (Lua egTeamReady: checked==0 -> true); the recoverMax ceiling
        // only bounds the case where a future cd read reports not-ready.
        if (s.TeamCdReady ?? true)
        {
            Phase = Lure;
            _at = s.NowMs;
            _reburst = 0;
            return Step(EndgameIntents.Hold, "", "time pronto - puxar a proxima wave");
        }
        if (s.NowMs - _recoverSince >= (long)System.Math.Max(0, c.RecoverMaxS) * 1000)
        {
            Phase = Lure;
            _at = s.NowMs;
            return Step(EndgameIntents.Hold, "", $"cooldown nao ficou 100% em {c.RecoverMaxS}s - puxando a wave assim mesmo");
        }
        int waited = (int)((s.NowMs - _recoverSince) / 1000);
        return Step(EndgameIntents.Hold, "", $"recuperando cooldown no safe spot ({waited}s/{Math.Max(0, c.RecoverMaxS)}s)");
    }

    // ----------------------------------------------------------------- utils
    private static int Chebyshev(int ax, int ay, int bx, int by) =>
        System.Math.Max(System.Math.Abs(bx - ax), System.Math.Abs(by - ay));

    private static readonly string[] Prefixes =
    { "shiny", "giant", "elite", "ancient", "elder", "dark", "crystal", "noel", "mega", "alpha" };

    internal static string Norm(string s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        int open = s.LastIndexOf('[');
        if (open >= 0 && s.EndsWith(']'))
        {
            var lvl = s[(open + 1)..^1].Trim();
            if (lvl.All(char.IsDigit)) s = s[..open].TrimEnd();
        }
        return s;
    }

    internal static string NormNP(string s)
    {
        s = Norm(s);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pfx in Prefixes)
            {
                if (s.StartsWith(pfx, StringComparison.Ordinal))
                {
                    var rest = s[pfx.Length..].TrimStart(' ');
                    if (rest.Length > 0) { s = rest; changed = true; break; }
                }
            }
        }
        return s;
    }
}
