using System.Globalization;
using System.Text;

namespace KBot.App.Services
{
    /// <summary>
    /// Diagnóstico de FORMATO da leitura de posição. Quando o offset DX/GL confirmado para de
    /// funcionar e o auto-calibrador acha zero candidatos, a causa quase sempre é de TIPO, não de
    /// endereço: o cliente guarda X/Y/Z como float/double/fixed-point e todos os leitores do core
    /// (read direto, delta-scan, offset-hunter) assumem int32. Este probe decodifica cada 4 bytes de
    /// uma janela de memória de TODAS as formas plausíveis e as coloca lado a lado, para o operador
    /// (ou o bot) conferir contra o valor do minimap.
    ///
    /// É 100% puro (byte[] -> texto): sem pipe, sem WPF, testável headless. A orquestração do dump
    /// vive no KBotLifecycle; aqui só interpretamos o que chegou.
    /// </summary>
    public static class PositionFormatProbe
    {
        public sealed record Word(int Offset, int Int32, string Float32, string FixedPoint);

        // Decode one window of memory (as raw little-endian bytes) into human-comparable lines.
        // startOffsetBytes is the offset of 'data[0]' relative to the position anchor (base+0x37454E0),
        // so the operator can find "+0 = X, +4 = Y, +8 = Z" at a glance even when the anchor is off.
        public static List<Word> Decode(byte[] data, int startOffsetBytes = 0)
        {
            var result = new List<Word>();
            if (data is null || data.Length < 4) return result;
            for (var i = 0; i + 4 <= data.Length; i += 4)
            {
                var value = BitConverter.ToInt32(data, i);
                var f = BitConverter.ToSingle(data.AsSpan(i, 4));
                result.Add(new Word(startOffsetBytes + i, value, FormatFloat(f), FormatFixed(value)));
            }
            return result;
        }

        // The old reader expects X/Y/Z at anchor offsets 0/4/8. Flag those three words so they stand out.
        private static bool IsAnchorTriple(int offset) => offset is 0 or 4 or 8;

        private static string FormatFloat(float f)
        {
            if (float.IsNaN(f)) return "nan";
            if (float.IsPositiveInfinity(f)) return "+inf";
            if (float.IsNegativeInfinity(f)) return "-inf";
            // Tile coordinates are small; more than 3 decimals means "this word is not a float coord".
            var rounded = Math.Round(f, 3);
            return Math.Abs(rounded) >= 1e5 ? "<big>" : rounded.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // Fixed-point guess: if the raw int is large, it likely encodes (coord * scale). Show the most
        // common scales so a "29622283" instantly reads as "2962.2283 x10000".
        private static string FormatFixed(int value)
        {
            if (value is 0) return "-";
            if (Math.Abs(value) < 50000) return "int?";
            return $"{value / 4096.0:0.###} / {value / 1000.0:0.###} / {value / 10000.0:0.###}";
        }

        /// <summary>
        /// Builds the full report: the anchor triple decoded, plus surrounding context, plus a
        /// short verdict on what format the position field most likely uses given a reference
        /// minimap value (when provided). Returns plain text sized for pasting back.
        /// </summary>
        public static string Report(List<Word> words, double? minimapReference = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DIAGNOSTICO DE POSICAO (dump em base+offset_antigo)");
            sb.AppendLine("colunas: off(int) | int32 | float32 | fixed( /4096 /1000 /10000 )");
            sb.AppendLine(new string('-', 66));

            if (words.Count == 0)
                return sb.AppendLine("nenhum dado valido lido (dump vazio ou pagina ilegivel)").ToString();

            foreach (var w in words)
            {
                var marker = IsAnchorTriple(w.Offset) ? "  <-- " : "";
                sb.AppendLine($"+{w.Offset,3:X}: {w.Int32,9} | {w.Float32,-7} | {w.FixedPoint}{marker}");
            }

            sb.AppendLine(new string('-', 66));

            if (minimapReference is { } val)
                sb.AppendLine($"referencia do minimap: {val.ToString("0.####", CultureInfo.InvariantCulture)}");
            sb.Append(Verdict(words, minimapReference));
            return sb.ToString();
        }

        // Best-effort read on the FORMAT, not on the address. We only say which TYPE matches the
        // reference (if any anchor word does); we never claim the final offset from this alone.
        private static string Verdict(List<Word> words, double? reference)
        {
            if (reference is not { } val || words.Count == 0)
                return Environment.NewLine + "sem referencia -> compare os int32/float32 acima com o minimap.";

            foreach (var w in words.Where(w => IsAnchorTriple(w.Offset)))
            {
                // int path: the reader already returns PosX as this int; if it equals the reference
                // rounded, int32 IS the format and the offset is simply stale/wrong elsewhere.
                if (Math.Abs(w.Int32 - val) < 0.5)
                    return $"\nveredito: INT32 confere (+{w.Offset:X} = {w.Int32}). Formato ok; o OFFSET que mudou, nao o tipo.";
            }
            return "\nveredito: nenhum campo no ancor 0/4/8 bate com o minimap como int32.\n" +
                   "             -> provavel FLOAT/DOUBLE/FIXED. Veja qual coluna confere e me mande este bloco.";
        }
    }
}
