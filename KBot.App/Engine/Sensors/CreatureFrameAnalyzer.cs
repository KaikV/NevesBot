using KBot.App.BotBrain;

namespace KBot.App.Engine.Sensors
{
    /// <summary>Tunable thresholds for on-screen creature detection. Centralized for future tuning.</summary>
    public sealed record CreatureScanParams
    {
        public int GridCols { get; init; } = 10;
        public int GridRows { get; init; } = 5;
        public int PlayerTileCol { get; init; } = 5;
        public int PlayerTileRow { get; init; } = 2;
        // A tile is "occupied" when a healthy FRACTION of its sprite band is a bright edge
        // (a rendered creature sprite). Background tiles have sparse, low-contrast edges.
        // Fraction-based (not absolute counts) so it behaves the same at any window size.
        public double MinSpriteFraction { get; init; } = .22;
        // Alive when the tile's top HP band is mostly filled with green.
        public double MinGreenHpFraction { get; init; } = .28;
        // Our summoned pokemon's HP bar is tinted blue; heavy blue in the band => SummonOwn (3).
        public double OwnBlueFraction { get; init; } = .18;
        public double BrightMinSum { get; init; } = 170;
        public int EdgeDelta { get; init; } = 90;
    }

    /// <summary>
    /// Turns a raw client frame into a list of <see cref="ScannedCreature"/>.
    ///
    /// Honest scope — this is the FIRST visual extractor. It works over the fixed
    /// visible-tile grid (~10x5 tiles centred on the player, matching main.lua's
    /// getSpectatorsInRange). Per occupied tile:
    ///   alive/HP : the top ~16% band is the HP bar; a high green fraction => alive (hp 100),
    ///              low => corpse (hp 0);
    ///   type     : a heavy blue tint in that band => SummonOwn (3) = our pokemon, else Type 1 (monster);
    ///   position : RELATIVE to the player (our pokemon sits at 0,0) — exactly what
    ///               <c>ScreenScan.DangerNearby</c> / <c>TargetSelection.Pick</c> consume.
    ///
    /// All occupancy signals are FRACTIONS of the sampled band, not raw pixel counts, so the
    /// detector is scale-invariant across window sizes. No name/ID yet (pending OCR /
    /// structured offsets) — those fields stay empty. Pure + deterministic: same pixels in
    /// => same creatures out, no clock, no IO.
    /// </summary>
    public static class CreatureFrameAnalyzer
    {
        public static readonly CreatureScanParams Default = new();

        public static IReadOnlyList<ScannedCreature> Extract(CapturedFrame frame, CreatureScanParams? p = null)
        {
            p ??= Default;
            var list = new List<ScannedCreature>();
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Bgra.Length == 0) return list;

            var colW = frame.Width / p.GridCols;
            var rowH = frame.Height / p.GridRows;
            if (colW <= 6 || rowH <= 6) return list;

            for (var r = 0; r < p.GridRows; r++)
                for (var c = 0; c < p.GridCols; c++)
                {
                    var (spriteFrac, greenHpFrac, blueHpFrac) =
                        SampleCell(frame, c * colW, r * rowH, colW, rowH, p);

                    if (spriteFrac < p.MinSpriteFraction) continue;

                    var type = blueHpFrac >= p.OwnBlueFraction ? ScannedCreature.SummonOwn : 1;
                    var alive = greenHpFrac >= p.MinGreenHpFraction;
                    var relX = c - p.PlayerTileCol;
                    var relY = r - p.PlayerTileRow;

                    list.Add(new ScannedCreature(type, alive ? 100 : 0, relX, relY, 0));
                }

            return list;
        }

        private static (double Sprite, double GreenHp, double BlueHp) SampleCell(
            CapturedFrame f, int left, int top, int w, int h, CreatureScanParams p)
        {
            var pad = Math.Max(2, w / 16);
            var hpBottom = top + (int)(h * .16);       // top ~16% = HP-bar band
            var sprTop = top + (int)(h * .20);
            var sprBottom = top + (int)(h * .72);

            // --- HP band (top): green/blue fraction => alive / own classification ---
            var hpTotal = 0; var hpGreen = 0; var hpBlue = 0;
            for (var y = top; y < hpBottom; y += 2)
                for (var x = left + pad; x < left + w - pad; x += 2)
                {
                    var (r, g, b) = f.Pixel(x, y);
                    hpTotal++;
                    if (IsGreen(r, g, b)) hpGreen++;
                    else if (IsBlue(r, g, b)) hpBlue++;
                }

            // --- Sprite band (middle): bright-edge fraction => occupied ---
            var sprTotal = 0; var sprBrightEdge = 0;
            for (var y = sprTop; y < sprBottom; y += 3)
                for (var x = left + pad; x < left + w - pad; x += 2)
                {
                    var (r, g, b) = f.Pixel(x, y);
                    if (r + g + b < p.BrightMinSum) continue;
                    sprTotal++;
                    if (IsBrightEdge(r, g, b, f, x, y, p.EdgeDelta)) sprBrightEdge++;
                }

            var sprite = sprTotal >= 8 ? (double)sprBrightEdge / sprTotal : 0;
            var green = hpTotal >= 8 ? (double)hpGreen / hpTotal : 0;
            var blue = hpTotal >= 8 ? (double)hpBlue / hpTotal : 0;
            return (sprite, green, blue);
        }

        private static bool IsBrightEdge(byte r, byte g, byte b, CapturedFrame f, int x, int y, int delta)
        {
            var (pr, pg, pb) = f.Pixel(Math.Max(0, x - 1), y);
            return Math.Abs(r - pr) + Math.Abs(g - pg) + Math.Abs(b - pb) > delta;
        }

        private static bool IsGreen(byte r, byte g, byte b) => g > 95 && g > r * 1.5 && g > b * 1.15;
        private static bool IsBlue(byte r, byte g, byte b) => b > 75 && b > r * 1.18 && b > g * 1.05;
    }
}
