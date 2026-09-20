using KBot.App.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KBot.App.Services;

public sealed record VisionRegion(string Name, double X, double Y, double Width, double Height, bool Detected);
public enum VisionCaptureStatus { Available, CaptureUnavailable }

public sealed record VisionEvidence(
    CharacterPresence State, double Confidence, string Status,
    int SignalCount, int SignalTotal, BitmapSource? Frame,
    IReadOnlyList<VisionRegion> Regions,
    VisionCaptureStatus CaptureStatus = VisionCaptureStatus.Available);

public sealed class VisionInGameDetector
{
    // Regions are fractions of the captured client area. The sidebar is anchored
    // to the right edge; the Pokebar is anchored to the left edge.
    private static readonly (string Name, double X, double Y, double W, double H)[] Areas =
    [
        ("Map", .19, .13, .58, .60),
        ("Pokebar", 0, .045, .14, .73),
        ("Battle List", .88, 0, .12, .32),
        ("Pokémon HUD", .88, .30, .12, .19),
        ("Action bar", .25, .92, .51, .08)
    ];
    private static readonly (string Name, double X, double Y, double W, double H) SelectionCard =
        ("Character card", .30, .32, .40, .36);
    private static readonly (string Name, double X, double Y, double W, double H) SelectionButton =
        ("Selection login", .43, .68, .14, .08);
    private static readonly (string Name, double X, double Y, double W, double H) SelectionStats =
        ("Character stats", 0, .13, .15, .26);

    public VisionEvidence Detect(GameSession game)
    {
        BitmapSource frame;
        try { frame = game.Capture(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or
            ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return new(CharacterPresence.Unknown, 0, $"Capture failed: {ex.Message}", 0, Areas.Length,
                null, Array.Empty<VisionRegion>(), VisionCaptureStatus.CaptureUnavailable);
        }
        try { return Analyze(frame); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            return new(CharacterPresence.Unknown, 0, $"Invalid frame: {ex.Message}", 0, Areas.Length,
                null, Array.Empty<VisionRegion>(), VisionCaptureStatus.CaptureUnavailable);
        }
    }

    public VisionEvidence Analyze(BitmapSource frame)
    {
        if (frame.PixelWidth < 500 || frame.PixelHeight < 350)
            return new(CharacterPresence.Unknown, 0, "Window too small", 0, Areas.Length,
                frame, Array.Empty<VisionRegion>(), VisionCaptureStatus.CaptureUnavailable);

        var converted = frame.Format == PixelFormats.Bgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var image = new PixelImage(pixels, converted.PixelWidth, converted.PixelHeight, stride);
        if (image.IsBlankFrame())
            return new(CharacterPresence.Unknown, 0, "Blank frame", 0, Areas.Length,
                frame, Array.Empty<VisionRegion>(), VisionCaptureStatus.CaptureUnavailable);
        var map = image.HasMapTexture(Areas[0]);
        var pokebar = image.CountColorBars(Areas[1], PixelImage.IsGreen, 2) >= 2 &&
                      image.CountColorBars(Areas[1], PixelImage.IsCyan, 2) >= 2;
        var battle = image.DarkFraction(Areas[2]) > .45 &&
                     image.CountColorBars(Areas[2], PixelImage.IsGreen, 1) >= 1;
        var pokemon = image.DarkFraction(Areas[3]) > .35 &&
                      image.CountColorBars(Areas[3], PixelImage.IsRed, 1) >= 1 &&
                      image.CountColorBars(Areas[3], PixelImage.IsBlue, 1) >= 1;
        var action = image.DarkFraction(Areas[4]) > .48 && image.EdgeFraction(Areas[4]) > .08;
        var signals = new[] { map, pokebar, battle, pokemon, action };
        var regions = Areas.Select((a, i) => new VisionRegion(a.Name, a.X, a.Y, a.W, a.H, signals[i])).ToArray();
        var count = signals.Count(value => value);
        var hudCount = new[] { pokebar, battle, pokemon, action }.Count(value => value);
        var inGame = map && hudCount >= 2 && (pokebar || pokemon) && count >= 3;
        var selectionButton = image.CountColorBars(SelectionButton, PixelImage.IsGreen, 1) > 0;
        var selectionCard = image.DarkFraction(SelectionCard) < .3 && image.EdgeFraction(SelectionCard) > .06;
        var selectionStats = image.DarkFraction(SelectionStats) > .3 && image.EdgeFraction(SelectionStats) > .07;
        var selection = !inGame && hudCount == 0 && selectionButton && selectionCard && selectionStats;
        if (selection)
            regions = [.. regions,
                new(SelectionCard.Name, SelectionCard.X, SelectionCard.Y, SelectionCard.W, SelectionCard.H, selectionCard),
                new(SelectionButton.Name, SelectionButton.X, SelectionButton.Y, SelectionButton.W, SelectionButton.H, selectionButton),
                new(SelectionStats.Name, SelectionStats.X, SelectionStats.Y, SelectionStats.W, SelectionStats.H, selectionStats)];
        var confidence = inGame ? Math.Min(.98, .68 + .06 * count) : selection ? .9 : 0;
        var details = string.Join(", ", regions.Select(r => $"{r.Name}={(r.Detected ? "yes" : "no")}"));
        return new(inGame ? CharacterPresence.InGame : selection ? CharacterPresence.CharacterSelection : CharacterPresence.Unknown, confidence,
            $"Capture OK; {count}/{Areas.Length} signals; {details}", count, Areas.Length, frame, regions);
    }

    private sealed class PixelImage(byte[] data, int width, int height, int stride)
    {
        public static bool IsGreen(byte r, byte g, byte b) => g > 95 && g > r * 1.5 && g > b * 1.15;
        public static bool IsCyan(byte r, byte g, byte b) => g > 110 && b > 105 && g > r * 1.5 && b > r * 1.4;
        public static bool IsRed(byte r, byte g, byte b) => r > 110 && r > g * 1.5 && r > b * 1.4;
        public static bool IsBlue(byte r, byte g, byte b) => b > 75 && b > r * 1.18 && b > g * 1.05;
        public delegate bool ColorMatch(byte r, byte g, byte b);

        private (int L, int T, int R, int B) Bounds((string, double X, double Y, double W, double H) area) =>
            ((int)(area.X * width), (int)(area.Y * height),
             Math.Min(width, (int)((area.X + area.W) * width)),
             Math.Min(height, (int)((area.Y + area.H) * height)));

        private (byte R, byte G, byte B) At(int x, int y)
        {
            var i = y * stride + x * 4;
            return (data[i + 2], data[i + 1], data[i]);
        }

        public bool IsBlankFrame()
        {
            var min = 255;
            var max = 0;
            var visible = 0;
            var samples = 0;
            for (var y = 0; y < height; y += Math.Max(1, height / 36))
                for (var x = 0; x < width; x += Math.Max(1, width / 64))
                {
                    var (r, g, b) = At(x, y);
                    var brightness = (r + g + b) / 3;
                    min = Math.Min(min, brightness);
                    max = Math.Max(max, brightness);
                    if (brightness > 20) visible++;
                    samples++;
                }
            return samples == 0 || max - min < 12 || (double)visible / samples < .005;
        }

        public int CountColorBars((string, double X, double Y, double W, double H) area, ColorMatch match, int limit)
        {
            var (left, top, right, bottom) = Bounds(area);
            var bars = 0;
            var lastBarY = -100;
            var runNeeded = Math.Max(10, (int)(width * .014 / 2));
            for (var y = top; y < bottom; y += 3)
            {
                var run = 0;
                var found = false;
                for (var x = left; x < right; x += 2)
                {
                    var (r, g, b) = At(x, y);
                    run = match(r, g, b) ? run + 1 : 0;
                    if (run >= runNeeded) { found = true; break; }
                }
                if (found && y - lastBarY > 9)
                {
                    bars++;
                    lastBarY = y;
                    if (bars >= limit) return bars;
                }
            }
            return bars;
        }

        public double DarkFraction((string, double X, double Y, double W, double H) area)
        {
            var (left, top, right, bottom) = Bounds(area);
            var dark = 0;
            var total = 0;
            for (var y = top; y < bottom; y += 8)
                for (var x = left; x < right; x += 8)
                {
                    var (r, g, b) = At(x, y);
                    if (r < 75 && g < 75 && b < 75) dark++;
                    total++;
                }
            return total == 0 ? 0 : (double)dark / total;
        }

        public double EdgeFraction((string, double X, double Y, double W, double H) area)
        {
            var (left, top, right, bottom) = Bounds(area);
            var edges = 0;
            var total = 0;
            for (var y = top + 8; y < bottom; y += 8)
                for (var x = left + 8; x < right; x += 8)
                {
                    var a = At(x, y);
                    var b = At(x - 8, y);
                    if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 70) edges++;
                    total++;
                }
            return total == 0 ? 0 : (double)edges / total;
        }

        public bool HasMapTexture((string, double X, double Y, double W, double H) area)
        {
            var (left, top, right, bottom) = Bounds(area);
            var bright = 0;
            var edges = 0;
            var total = 0;
            for (var y = top + 10; y < bottom; y += 10)
                for (var x = left + 10; x < right; x += 10)
                {
                    var a = At(x, y);
                    var b = At(x - 10, y);
                    if (a.R + a.G + a.B > 135) bright++;
                    if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 60) edges++;
                    total++;
                }
            return total > 0 && (double)bright / total > .35 && (double)edges / total > .15;
        }
    }
}
