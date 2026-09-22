using KBot.App.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KBot.App.Services
{
    // Self-healing position-offset calibration. After a client update the saved
    // (or compiled-in) offset breaks and the reader degrades to NOT_CONFIGURED.
    // This calibrator re-discovers the X/Y/Z triple with zero human input:
    //   1. SNAP    - the core caches the whole main module (character standing still)
    //   2. STEP    - one tile via the key pipe (D / RIGHT / UP fallbacks)
    //   3. COMMIT  - diff the snapshot; only triples where EXACTLY one axis moved
    //                by one tile are candidates
    //   4. STABLE  - the character stands still again; candidates that kept moving
    //                are counters and get dropped
    //   5. VERIFY  - apply each survivor, read the position twice and require two
    //                identical READY reads before persisting the winning offset
    public sealed class OffsetAutoCalibrator
    {
        private readonly object _gate = new();
        private bool _running;

        public string Status { get; private set; } = "";

        public void ReportStatus(string value) => SetStatus(value);

        public bool IsRunning
        {
            get { lock (_gate) return _running; }
        }

        public async Task<bool> RunAsync(NativeService native, string executableName,
            PositionOffsetStore store, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_running) return false;
                _running = true;
            }
            try
            {
                var commitCandidates = new List<long>();
                var keysTried = new List<string>();
                string? workedKey = null;
                for (var attempt = 0; attempt < 3 && commitCandidates.Count == 0; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = attempt switch { 0 => "D", 1 => "RIGHT", _ => "UP" };
                    keysTried.Add(key);
                    SetStatus($"Snapshot da memória (tentativa {attempt + 1}/3)...");
                    if (!JsonOk(await native.ScanDeltaSnapAsync(cancellationToken))) continue;

                    SetStatus($"Passando 1 tile com a tecla [{key}] para gerar o delta...");
                    if (!await native.SendKeyAsync(key, cancellationToken)) continue;
                    await Task.Delay(1200, cancellationToken);

                    commitCandidates = ParseCandidates(await native.ScanDeltaCommitAsync(cancellationToken));
                    workedKey = commitCandidates.Count > 0 ? key : null;
                }
                if (workedKey is null)
                {
                    SetStatus($"Nenhum candidato: o passo com {string.Join("/", keysTried)} não produziu variação legível.");
                    return false;
                }

                SetStatus("Personagem parado; filtrando candidatos instáveis...");
                await Task.Delay(2000, cancellationToken);
                var stableSet = ParseCandidates(await native.ScanDeltaStableAsync(cancellationToken)).ToHashSet();
                var survivors = StableSurvivors(commitCandidates, stableSet);
                if (survivors.Count == 0)
                {
                    SetStatus("Todos os candidatos do passo se mostraram instáveis; provável falso positivo. Falha limpa.");
                    return false;
                }

                foreach (var offset in survivors)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SetStatus($"Validando candidato 0x{offset:X} com duas leituras...");
                    await native.SetPositionOffsetAsync(offset, cancellationToken);
                    var first = await native.GetStatusAsync(cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                    var second = await native.GetStatusAsync(cancellationToken);
                    var verified = IsVerifiedRead(first) && IsVerifiedRead(second) &&
                        first!.PosX == second!.PosX && first.PosY == second.PosY && first.PosZ == second.PosZ;
                    if (!verified) continue;

                    var backKey = workedKey switch { "D" => "A", "RIGHT" => "LEFT", _ => "DOWN" };
                    await native.SendKeyAsync(backKey, cancellationToken); // give the stepped tile back
                    store.Set(executableName, offset);
                    SetStatus($"Offset 0x{offset:X} recalibrado e salvo para {executableName}.");
                    return true;
                }

                SetStatus("Nenhum candidato sobreviveu à leitura dupla (posição instável ou ilegível).");
                return false;
            }
            finally
            {
                lock (_gate) _running = false;
            }
        }

        private void SetStatus(string value)
        {
            Status = value;
            Trace.WriteLine($"[OffsetAutoCalibrator] {value}");
        }

        // ---- pure, testable helpers ----------------------------------------

        public static bool IsVerifiedRead(NativeStatus? status) =>
            status is { ReaderStatus: "READY", HasPosition: true };

        // Parses the core's {"count":N,"candidates":["0x.."]} response (or any
        // JSON object with a "candidates" array) into offsets from base. Tokens
        // are 0x-hex or plain decimal; anything outside [0, 0x8000000] is junk.
        public static List<long> ParseCandidates(string? json)
        {
            var result = new List<long>();
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!property.Name.Equals("candidates", StringComparison.OrdinalIgnoreCase) ||
                        property.Value.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        long value;
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
                            value = ParseOffsetToken(text);
                        else if (item.ValueKind == JsonValueKind.Number)
                            value = item.GetInt64();
                        else continue;
                        if (value >= 0 && value <= 0x8000000) result.Add(value);
                    }
                    break;
                }
            }
            catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or ArgumentException) { }
            return result;
        }

        // Same magnitude guard the core applies: a tile coordinate never gets
        // anywhere near five orders of magnitude.
        public static bool IsSaneTriple(int x, int y, int z) =>
            Math.Abs(x) < 50000 && Math.Abs(y) < 50000 && Math.Abs(z) < 50000;

        // Commit-order-preserving intersection with the stable list: a candidate
        // survives only if the core saw it frozen while the character stood still.
        public static List<long> StableSurvivors(IReadOnlyList<long> commitCandidates, ISet<long> stableOffsets)
        {
            var survivors = new List<long>();
            foreach (var offset in commitCandidates)
                if (stableOffsets.Contains(offset) && !survivors.Contains(offset))
                    survivors.Add(offset);
            return survivors;
        }

        public static long PickCandidate(IReadOnlyList<long> survivors) =>
            survivors.Count == 0 ? 0 : survivors[0];

        // True only when the core answered {"ok":true,...}.
        public static bool JsonOk(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                using var document = JsonDocument.Parse(json);
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name.Equals("ok", StringComparison.OrdinalIgnoreCase))
                        return property.Value.ValueKind == JsonValueKind.True;
            }
            catch (JsonException) { }
            return false;
        }

        private static long ParseOffsetToken(string text)
        {
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt64(text.Substring(2), 16);
            return long.Parse(text, System.Globalization.NumberStyles.Integer);
        }
    }
}
