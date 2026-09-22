namespace KBot.App.Engine.Sensors
{
    /// <summary>
    /// A captured client frame as raw BGRA pixels plus a validity flag.
    /// Pure data (no WPF types) so it is testable headless.
    /// </summary>
    public sealed record CapturedFrame(int Width, int Height, int Stride, byte[] Bgra)
    {
        public bool IsBlank { get; init; }

        public static readonly CapturedFrame Empty = new(0, 0, 0, Array.Empty<byte>());

        public byte At32(int x, int y, int channel) => Bgra[y * Stride + x * 4 + channel];
        public (byte R, byte G, byte B) Pixel(int x, int y)
        {
            var i = y * Stride + x * 4;
            return (Bgra[i + 2], Bgra[i + 1], Bgra[i]);
        }
    }

    /// <summary>Result of a capture attempt — separates "captured" from "unavailable".</summary>
    public enum CaptureOutcome
    {
        /// A real frame was produced (may be blank).
        Ok,
        /// No frame could be produced (bad window, minimized, GDI failure). Distinct from an empty world.
        Unavailable
    }

    public sealed record CaptureResult(CaptureOutcome Outcome, CapturedFrame? Frame, string? Error = null);

    /// <summary>
    /// Produces a raw client frame. The Windows implementation wraps the existing
    /// <c>WindowCaptureService</c>; tests supply synthetic frames. Keeping this behind an
    /// interface is what makes the pixel logic testable without a display.
    /// </summary>
    public interface IFrameSource
    {
        CaptureResult Capture();
    }
}
