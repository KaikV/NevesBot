using System.Windows.Media;
using System.Windows.Media.Imaging;
using KBot.App.Services;

namespace KBot.App.Engine.Sensors
{
    /// <summary>
    /// Real client capture on Windows. Thin adapter over the existing
    /// <see cref="WindowCaptureService"/> (PrintWindow DWM → GDI → BitBlt). Owns no new
    /// capture logic: it just converts the frozen <see cref="BitmapSource"/> into raw
    /// BGRA bytes so the downstream pixel analysis stays WPF-free and testable.
    /// A failed capture reports <see cref="CaptureOutcome.Unavailable"/> — never an empty world.
    /// </summary>
    public sealed class GameWindowFrameSource : IFrameSource
    {
        private readonly Func<nint> _hwnd;

        public GameWindowFrameSource(Func<nint> hwnd)
        {
            _hwnd = hwnd ?? throw new ArgumentNullException(nameof(hwnd));
        }

        public CaptureResult Capture()
        {
            var handle = _hwnd();
            if (!WindowCaptureService.IsWindow(handle))
                return new CaptureResult(CaptureOutcome.Unavailable, null, "invalid or closed window handle");

            BitmapSource? source = null;
            try
            {
                source = WindowCaptureService.Capture(handle);
                var converted = source.Format == PixelFormats.Bgra32
                    ? source
                    : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                var stride = converted.PixelWidth * 4;
                var pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);
                return new CaptureResult(CaptureOutcome.Ok, new CapturedFrame(converted.PixelWidth, converted.PixelHeight, stride, pixels));
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or ArgumentException
                or System.Runtime.InteropServices.ExternalException)
            {
                return new CaptureResult(CaptureOutcome.Unavailable, null, ex.Message);
            }
        }
    }
}
