using KBot.App.Models;

namespace KBot.App.BotBrain;

// Port of n9_socorro.lua - "you must never be left without a poke on the field".
// Priority 95: below survival-heal (100), above combat auto-summon (90), so this
// is the authoritative sender when the field goes empty.
//
// WHY IT EXISTS (the real death it guards against): the pokebar is a LOCAL COPY
// that can lie. On 24/08 the bar showed the poke ALIVE (it had been recalled, not
// fainted), so auto-revive (which only acts on a fainted poke) stayed idle, and
// the player died ~20s later. This net does NOT trust the pokebar and does NOT
// depend on a toggle: if there is nothing on the field for a few seconds, it sends
// a poke out. Being poke-less is the only situation where the player dies for free.
//
// WHAT IT USES (the honest signals, in order of trust):
//   * FieldHasPoke (sofaPokeOut): is one of OUR pokemon visible on screen right now?
//     A creature with hp<=0 does NOT count (the client draws the corpse a beat before
//     the server removes it - counting it held the rescue exactly when it was needed).
//   * The pokebar, ONLY to pick WHICH slot to send - never to decide if we're covered.
//
// FAITHFUL RULES PORTED (each traced to n9_socorro.lua / main.lua):
//   1. Act only after the field has been empty for a FIXED GRACE of 1.5s
//      (ESPERA_MS). Danger never shortens the grace - sofaPokeOut already filters
//      invisibility/moves-bar, and acting sooner risks recalling a poke that was
//      there all along, since changePokemon is a TOGGLE.
//   2. Slot choice, in trust order: active slot if alive > first alive > never guess.
//      While our own poke is FAINTED and a revive is configured, YIELD to the revive
//      for the first 6s of the absence (REVIVE_CARENCIA_MS) - swapping would strand
//      the dead poke in the ball. After that window, a live swap beats no poke.
//   3. A slot we sent that did not bring a poke is marked FAILED (8s), and the NEXT
//      attempt goes immediately (no rhythm wait). If every slot is marked, the list
//      clears and we retry - never giving up.
//   4. Rhythm between attempts is of the DANGER, not the clock: 1.2s near a wild,
//      4s otherwise. A cap of MAX_ATTEMPTS per absence prevents a flood.
public sealed class SocorroModule : IBotModule
{
    public string Name => "Socorro";
    public int Priority => 95;

    private const long GraceMs = 1500;         // ESPERA_MS: 1.5s with nothing on screen is enough certainty
    private const long RhythmMsNormal = 4000;  // RITMO_MS: server needs a beat between swaps
    private const long RhythmMsDanger = 1200;  // RITMO_SOS_MS: a wild glued to us shrinks the rhythm
    private const long ConfirmMs = 1200;       // SOLTA_CONFIRMA_MS: window for a sent poke to appear
    private const long FailedMs = 8000;        // validity of a "liar" bench (a slot may come back to life)
    private const int MaxAttempts = 5;         // MAX_TENT: if even these fail, something else is wrong
    private const long ReviveYieldMs = 6000;   // REVIVE_CARENCIA_MS: identity window - yield while <= this

    private long? _sinceEmptyMs;
    private int _attemptsThisAbsence;
    private int? _pendingConfirmSlot;
    private long _pendingConfirmAtMs;
    private long _lastAttemptMs;
    private bool _needsImmediate;   // last send failed -> next goes with no rhythm wait
    private readonly Dictionary<int, long> _failedUntilMs = new();
    public string Status { get; private set; } = "";

    public ActionIntent? Decide(GameState s, IProfileView p)
    {
        if (!s.InGame) return null;

        // Unknown (vision not reading yet) means COVERED, not empty - exactly what
        // sofaPokeOut() does (`fora ~= false` => reset). Acting while the screen is
        // still unread would turn every blind boot into a changePokemon toggle storm.
        bool pokeOnField = s.FieldHasPoke != false;

        if (pokeOnField)
        {
            // Healthy: reset the whole absence machine, including the failure
            // bench - the incident is over, so nobody stays benched from it.
            _sinceEmptyMs = null;
            _attemptsThisAbsence = 0;
            _pendingConfirmSlot = null;
            _needsImmediate = false;
            _failedUntilMs.Clear();
            return null;
        }

        // Field empty (our pokemon not visible). Start the clock if fresh.
        if (_sinceEmptyMs is null)
        {
            _sinceEmptyMs = s.NowMs;
            _attemptsThisAbsence = 0;
            _pendingConfirmSlot = null;
            SetStatus("sem poke em campo...");
            return null;
        }

        bool danger = s.WildsNearby > 0;

        // Grace window before we believe it. Fixed at ESPERA_MS regardless of
        // danger: sofaPokeOut already filters invisibility/moves-bar, so 1.5s of
        // an empty screen is enough certainty (a shorter one risks recalling a
        // poke that was there all along - changePokemon is a TOGGLE).
        if (s.NowMs - _sinceEmptyMs.Value < GraceMs)
        {
            SetStatus("sem poke em campo...");
            return null;
        }

        // A pending send that has had its confirm window and STILL nothing on the
        // field: that slot lied (pokebar said alive, nothing appeared). Bench it and
        // release the rhythm gate so the next attempt goes immediately.
        if (_pendingConfirmSlot is { } pending && s.NowMs - _pendingConfirmAtMs >= ConfirmMs)
        {
            _failedUntilMs[pending] = s.NowMs + FailedMs;
            _pendingConfirmSlot = null;
            _needsImmediate = true;
        }
        // Still inside the confirm window for the last send -> don't spam.
        if (_pendingConfirmSlot is not null)
            return null;

        // Cap: give up for this absence if even the maximum attempts failed.
        if (_attemptsThisAbsence >= MaxAttempts)
        {
            SetStatus($"sem poke e {MaxAttempts} tentativas falharam");
            return null;
        }

        long rhythm = danger ? RhythmMsDanger : RhythmMsNormal;
        // Pace between attempts UNLESS the previous send just failed (go now).
        bool skipRhythm = _needsImmediate || _lastAttemptMs == 0;
        if (!skipRhythm && s.NowMs - _lastAttemptMs < rhythm)
            return null;
        _needsImmediate = false;

        // The revive owns the identity of OUR poke for the first few seconds; once
        // that window passes (item out, server refusing), sending any alive poke is
        // better than taking hits bare-handed - so the yield stops applying.
        bool allowOther = s.NowMs - _sinceEmptyMs.Value > ReviveYieldMs;

        if (!TryChooseSlot(s, p, allowOther, out int slot, out string reason))
        {
            SetStatus(reason);
            return null; // NEVER guess a slot mid-cave.
        }

        _attemptsThisAbsence++;
        _lastAttemptMs = s.NowMs;
        _pendingConfirmSlot = slot;
        _pendingConfirmAtMs = s.NowMs;
        SetStatus($"solta slot {slot} ({reason})");
        return ActionIntent.Command("summon", slot.ToString());
    }

    // Trust order: active slot (alive) > first alive. For the first REVIVE_CARENCIA_MS
    // of an absence it yields to the revive when OUR poke is fainted (swapping would
    // strand the dead poke in the ball - real log 25/08); after that window it stops
    // yielding, because an item that ran out would leave the player with NO poke.
    private bool TryChooseSlot(GameState s, IProfileView p, bool allowOther, out int slot, out string reason)
    {
        slot = 0; reason = "";
        var pb = s.Pokebar;
        if (pb.Count == 0)
        {
            reason = "sem leitura da barra de pokes";
            return false;
        }

        bool Failed(int i)
        {
            return _failedUntilMs.TryGetValue(i, out var until) && until > s.NowMs;
        }
        bool RawAlive(int i) // i is the 1-based slot
        {
            var b = pb[i - 1];
            return b.IsValid && !b.IsFainted;
        }
        bool Alive(int i) => RawAlive(i) && !Failed(i);

        // 1) Our own poke is fainted and a revive is configured -> the revive owns it,
        //    but ONLY inside the identity window. Never returns a slot the pokebar says
        //    is down (that is exactly the lie that killed the player on 26/08).
        if (!allowOther && p.AutoRevive && !string.IsNullOrWhiteSpace(p.ReviveHotkey) &&
            s.ActivePokebarSlot is { } active && active >= 1 && active <= pb.Count &&
            pb[active - 1].IsValid && pb[active - 1].IsFainted)
        {
            reason = $"seu poke (slot {active}) está MORTO - quem resolve é o revive";
            return false;
        }

        // 2) The slot the game believes is out, if it is genuinely alive.
        if (s.ActivePokebarSlot is { } a2 && a2 >= 1 && a2 <= pb.Count && Alive(a2))
        {
            slot = a2; reason = "slot ativo";
            return true;
        }
        // Profile override slot (when the user pinned one).
        if (p.ActiveSlot >= 1 && p.ActiveSlot <= pb.Count && Alive(p.ActiveSlot))
        {
            slot = p.ActiveSlot; reason = "slot configurado";
            return true;
        }

        // 3) First alive slot in the bar.
        for (int i = 1; i <= pb.Count; i++)
            if (Alive(i)) { slot = i; reason = "primeiro vivo da barra"; return true; }

        // 4) Nothing alive, but some are only benched as "liars": clear the bench
        //    and try again - insisting blindly is bad, giving up is worse.
        if (_failedUntilMs.Count > 0)
        {
            _failedUntilMs.Clear();
            for (int i = 1; i <= pb.Count; i++)
                if (RawAlive(i)) { slot = i; reason = "esquecendo as falhas"; return true; }
        }

        reason = "nenhum poke vivo na barra";
        return false;
    }

    private void SetStatus(string v)
    {
        if (Status == v) return;
        Status = v;
    }
}
