namespace KBot.App.BotBrain;

// ============================================================================
//  ALERTAS - pure decision layer (port of n3_alarmes.lua `sofaALExtra`)
// ---------------------------------------------------------------------------
//  The Kryon alarm engine does NOT create eight loose features: it is ONE rule
//  engine where each type is an EDGE DETECTOR - it only fires on the transition
//  instant (a 4s cooldown then stops repeats). Those edge detectors are pure
//  state machines that need nothing but a scalar each tick, which is what lets
//  them be unit-tested headless with an injected clock/value.
//
//  Everything here is free of IO and of any memory offset. The transport that
//  feeds them (bag count, level, spectators, chat text, online stamp) arrives
//  later; until then they degrade to "no signal -> no alert", exactly like the
//  rest of the brain.
// ============================================================================

// ---- PROVA DA CAPTURA: a mensagem do SERVIDOR ------------------------------
// Port of `sofaCapturaDoTexto`. The ONLY honest signal that a catch happened is
// the server chat line:  "You caught a Pokemon! (Shiny Hitmonchan)".  The name
// lives inside the parentheses - that is where both the species and the "shiny"
// prefix come from, instead of guessing from a corpse disappearing off the floor
// (which loot/despawn/failed-ball also trigger - the Vaporeon-reported-5x bug).
public readonly record struct CatchParse(bool IsCatch, string Name, bool Shiny);

public static class CatchMessage
{
    // Lowercased fragments (substring match, like the Lua `find(...,1,true)`).
    private static readonly string[] Phrases = { "caught a pok", "capturou um pok" };

    public static CatchParse Parse(string src, string? sender, string? text)
    {
        var t = text ?? string.Empty;
        if (t.Length == 0) return new CatchParse(false, "", false);
        var low = t.ToLowerInvariant();

        // Only the SERVER may say we caught something. A named player saying the
        // same phrase is ignored - otherwise anyone shouting "You caught a Pokemon!
        // (Shiny Mewtwo)" in local would fire our alert (and the Telegram).
        bool doServidor = src == "text" || (src == "talk" && string.IsNullOrEmpty(sender));

        bool bate = false;
        foreach (var f in Phrases)
            if (low.Contains(f, System.StringComparison.Ordinal)) { bate = true; break; }

        if (!bate || !doServidor) return new CatchParse(false, "", false);

        // Name between parentheses: "(Shiny Hitmonchan)" -> "Shiny Hitmonchan".
        var nome = ExtractParens(t);
        nome = nome.Trim();
        bool shiny = nome.ToLowerInvariant().Contains("shiny", System.StringComparison.Ordinal);
        return new CatchParse(true, nome, shiny);
    }

    private static string ExtractParens(string t)
    {
        int a = t.IndexOf('(');
        int b = a >= 0 ? t.IndexOf(')', a) : -1;
        if (a < 0 || b < 0 || b < a) return string.Empty;
        return t.Substring(a + 1, b - a - 1);
    }
}

// The Lua keeps a small QUEUE (not a flag) because in a wave two catches can land
// between two scans and a flag would drop the second - the whole point of the
// rare-pokemon alert is that the rarer one MUST NOT vanish. A fresh line arriving
// twice inside 1.5s is ONE catch, not two.
public sealed class CatchFeed
{
    public const long DedupMs = 1500;
    private const int Cap = 40;

    private readonly Queue<CatchParse> _q = new();
    private string _lastTxt = string.Empty;
    private long _lastT = long.MinValue;

    public int Count => _q.Count;

    // Feed one chat line. Returns the parsed catch when it counts as a new one,
    // or IsCatch=false when it is a duplicate / non-catch line.
    public CatchParse Offer(string src, string? sender, string? text, long nowMs)
    {
        var p = CatchMessage.Parse(src, sender, text);
        if (!p.IsCatch) return p;
        if (text == _lastTxt && nowMs - _lastT < DedupMs)
            return new CatchParse(false, "", false);   // same line twice = one catch
        _lastTxt = text ?? string.Empty;
        _lastT = nowMs;
        _q.Enqueue(p);
        while (_q.Count > Cap) _q.Dequeue();
        return p;
    }
}

// ---- SUBIU DE NIVEL ---------------------------------------------------------
// Rising edge of the CHARACTER level. The first tick only records (otherwise
// logging in at level 300 would immediately say "leveled up").
public sealed class LevelUpTracker
{
    private int? _last;
    public int? Last => _last;
    public bool Tick(int? level)
    {
        if (level is null) return false;
        var antes = _last;
        _last = level;
        return antes.HasValue && level > antes.Value;
    }
}

// ---- MORTE DO PERSONAGEM ----------------------------------------------------
// Rising edge of dead (alive == false). Resets when alive again so it can fire a
// second death. A null (unknown hp) neither fires nor resets - we do not guess.
public sealed class DeathTracker
{
    private bool _dead;
    public bool Fire(bool? alive)
    {
        if (alive is false) { if (!_dead) { _dead = true; return true; } return false; }
        if (alive is true) _dead = false;
        return false;
    }
}

// ---- JOGADOR SAIU DA TELA ---------------------------------------------------
// Falling edge of "another player is on screen": there WAS someone, now there is
// not. Returns the who-seen (for the detail message) on the transition, else null.
public sealed class PresenceTracker
{
    private string? _had;
    public string? Tick(bool present, string? who)
    {
        var antes = _had;
        _had = present ? (who ?? "?") : null;
        return antes != null && !present ? antes : null;
    }
}

// ---- SUPRIMENTO ACABANDO ----------------------------------------------------
// FALLING edge + confirmation. Two symptoms the owner complained about fixed the
// design: (a) "avisa toda hora" - it was a LEVEL rule firing every cooldown while
// n<=min; now it is one alert per deplection. (b) "avisa com estoque cheio" - an
// item in a CLOSED bag reads 0 always; a 0 was never ABOVE min so a falling edge
// never crosses it -> no alert. A null (can't count: bag closed / wrong id / never
// seen) means SILENCE, not "empty". Confirmation: three consecutive low readings
// (~2s of scans) before firing, and it only re-arms once the count goes back above
// the minimum.
public sealed class SupplyTracker
{
    private int? _prev;
    private int _low;
    public string? Tick(int? count, int min)
    {
        if (count is null) return null;          // "não sei contar" -> quiet
        if (count.Value > min) { _low = 0; _prev = count; return null; }  // re-arms
        _low++;
        var msg = (_low == 3 && _prev.HasValue) ? $"suprimento: {count.Value} (minimo {min})" : null;
        _prev = count;
        return msg;
    }
}
