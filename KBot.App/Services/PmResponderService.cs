using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KBot.App.Services;

public sealed record PmSend(string To, string Message);

// Port of nF9_responderpm.lua. Pure decision layer: given a private message it
// schedules exactly ONE short reply per player (3-6s later). The client hook is
// what feeds OnPrivateMessage; the transport that actually writes the whisper is
// the app's concern (TalkChannel/whisper via pipe). GM private messages must NOT
// reach here (mode check happens at the hook, not inside this class).
public sealed class PmResponderService
{
    private const int MinDelayMs = 3000;
    private const int MaxDelayMs = 6000;
    private static readonly Random Rng = new();

    public string DefaultPhrases { get; } =
        "jaja, projeto hoje ...., depende do meu humor, rapaz...., " +
        "n aguento mais, daqui a pouco, mais tarde eu veja, kkkk";

    private sealed record Rule(int Priority, string Name, IReadOnlyList<string> Keys, IReadOnlyList<string> Replies)
    {
        public static readonly Rule[] All =
        {
            new(3, "presenca",
                new[] { "ta ai","ta ae","ta on","ta online","ta vivo","ta aqui","esta ai","ta af","responde","ta acordado","ta na frente" },
                new[] { "to","to sim","to aqui","to on" }),
            new(3, "demora",
                new[] { "vai demorar","demora","vai ficar muito","quanto tempo","ate que horas","vai sair","ta saindo","fica ate","vai ficar ate","muito tempo" },
                new[] { "sim","vou ficar um bom tempo","por enquanto sim" }),
            new(3, "oquefaz",
                new[] { "ta fazendo","fazendo oq","fazendo o que","ta cacando","ta na hunt","onde vc ta","onde ta","que hunt","qual hunt" },
                new[] { "projetin shiny","projeto hoje ....","to na hunt","cacando aqui" }),
            new(1, "saudacao",
                new[] { "salve","oi","ola","opa","eae","e ai","fala","bom dia","boa tarde","boa noite","blz","beleza" },
                new[] { "salve","opa","fala","salve salve" }),
        };
    }

    private readonly List<Rule> _rules = Rule.All.Select(r => r with { Keys = r.Keys.Select(Normalize).ToArray() }).ToList();
    private readonly Dictionary<string, bool> _answered = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Name, long DueAt, Rule? Rule)> _queue = new();

    public void OnPrivateMessage(string sender, string text, long nowMs, string? myName = null)
    {
        var name = sender.Trim();
        if (name.Length == 0) return;
        if (!string.IsNullOrEmpty(myName) && name.Equals(myName, StringComparison.OrdinalIgnoreCase)) return; // own echo

        var rule = Understand(text);
        var key = name.ToLowerInvariant();

        // Collection window: same player already waiting -> merge into that slot
        // (upgrade rule only; never extend the deadline).
        var existing = _queue.FindIndex(q => q.Name.ToLowerInvariant() == key);
        if (existing >= 0)
        {
            var cur = _queue[existing];
            if (rule is not null && (cur.Rule is null || rule.Priority > cur.Rule!.Priority))
                _queue[existing] = (cur.Name, cur.DueAt, rule);
            return;
        }

        if (_answered.ContainsKey(key)) return; // second message: silence on purpose
        _answered.Add(key, true);
        var delay = MinDelayMs + Rng.Next(MaxDelayMs - MinDelayMs);
        _queue.Add((name, nowMs + delay, rule));
    }

    public IEnumerable<PmSend> Tick(long nowMs, string phrasesCsv)
    {
        var ready = _queue.Where(q => nowMs >= q.DueAt).ToList();
        _queue.RemoveAll(q => nowMs >= q.DueAt);
        var phrases = (IReadOnlyList<string>)ParsePhrases(phrasesCsv);
        foreach (var q in ready)
        {
            var rule = q.Rule;
            IReadOnlyList<string> pool = rule != null ? rule.Replies : phrases;
            if (pool.Count == 0) continue;
            yield return new PmSend(q.Name, pool[Rng.Next(pool.Count)]);
        }
    }

    public void Reset()
    {
        _answered.Clear();
        _queue.Clear();
    }

    private Rule? Understand(string rawText)
    {
        var clean = Normalize(rawText);
        if (clean.Length == 0) return null;
        Rule? best = null;
        foreach (var rule in _rules.OrderByDescending(r => r.Priority))
        {
            if (rule.Keys.Any(k => System.Text.RegularExpressions.Regex.IsMatch(clean, "\\b" + System.Text.RegularExpressions.Regex.Escape(k) + "\\b"))) { best = rule; break; }
        }
        return best;
    }

    public static IList<string> ParsePhrases(string csv)
    {
        var outp = new List<string>();
        foreach (var piece in csv.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = piece.Trim();
            if (f.Length > 0) outp.Add(f);
        }
        return outp;
    }

    // Accents out first, then lowercase (A-Z only), then punctuation->space,
    // then collapse repeated letters ("taaaa aii" -> "ta a") until stable.
    public static string Normalize(string? text)
    {
        var s = text ?? string.Empty;
        s = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            var t = CharUnicodeInfo.GetUnicodeCategory(c);
            if (t != UnicodeCategory.NonSpacingMark && t != UnicodeCategory.SpacingCombiningMark) sb.Append(c);
        }
        s = sb.ToString().Normalize(NormalizationForm.FormC);
        var low = new StringBuilder(s.Length);
        foreach (var c in s) low.Append(char.IsUpper(c) ? char.ToLowerInvariant(c) : c);
        s = low.ToString();
        s = System.Text.RegularExpressions.Regex.Replace(s, "[^a-z\\s]", " ");
        string before;
        do { before = s; s = System.Text.RegularExpressions.Regex.Replace(s, "(.)\\1+", "$1"); } while (s != before);
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
        return s;
    }
}
