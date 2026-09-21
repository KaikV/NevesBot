using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KBot.App.Services;

public static class WindowCaptureService
{
    private const uint PrintWindowClientOnly = 0x00000001;
    // Requests the full DWM-rendered content (mandatory since Win8.1 for
    // DirectComposition/DX windows).
    private const uint PrintWindowRenderFullContent = 0x00000002;

    public static bool IsWindow(nint handle) => handle != 0 && IsWindowNative(handle);

    public static string GetWindowTitle(nint handle)
    {
        if (handle == 0) return string.Empty;
        var length = GetWindowTextLength(handle);
        var buffer = new StringBuilder(length + 1);
        GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }
    public static BitmapSource Capture(nint handle)
    {
        if (!GetClientRect(handle, out var rect)) throw new InvalidOperationException("Não foi possível obter os limites da janela.");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) throw new InvalidOperationException("A janela do cliente ainda não tem tamanho válido.");

        var windowDc = GetDC(handle);
        if (windowDc == 0) throw new InvalidOperationException("Não foi possível acessar a imagem da janela.");
        var memoryDc = CreateCompatibleDC(windowDc);
        var bitmap = CreateCompatibleBitmap(windowDc, width, height);
        var previous = SelectObject(memoryDc, bitmap);
        try
        {
            // 1) Ask DWM for the fully composited content (works for DX9/DX11
            //    windows since Win8.1).
            PrintWindow(handle, memoryDc, PrintWindowClientOnly | PrintWindowRenderFullContent);
            // 2) Fallback: plain GDI print of the window's own DC.
            if (IsBlank(memoryDc, width, height))
                PrintWindow(handle, memoryDc, 0);
            // 3) Last resort: BitBlt from the screen at the window's position
            //    (required when the game is exclusive fullscreen and DWM has
            //    no representation of the surface).
            if (IsBlank(memoryDc, width, height))
                TryBlitFromScreen(handle, memoryDc, width, height);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, nint.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(handle, windowDc);
        }
    }

    private static void TryBlitFromScreen(nint handle, nint destDc, int width, int height)
    {
        if (!GetWindowRect(handle, out var winRect)) return;
        GetWindowRect(nint.Zero, out var desktop);
        var sx = winRect.Left - desktop.Left;
        var sy = winRect.Top - desktop.Top;
        var hdcScreen = GetDC(nint.Zero);
        if (hdcScreen == 0) return;
        try
        {
            BitBlt(destDc, 0, 0, width, height, hdcScreen, sx, sy, SourceCopy);
        }
        finally
        {
            ReleaseDC(nint.Zero, hdcScreen);
        }
    }

    private static bool IsBlank(nint dc, int width, int height)
    {
        var bytes = width * height * 4;
        var buffer = new byte[bytes];
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = 40, // BITMAPINFOHEADER fixed size
                Width = width,
                Height = -height, // top-down
                Planes = 1,
                BitCount = 32,
            }
        };
        var ok = GetDIBits(dc, nint.Zero, 0, (uint)height, buffer, ref info, DIB_RGB_COLORS);
        if (!ok) return false; // unknown -> let caller keep result
        for (var i = 0; i < buffer.Length; i += 4 * 7)
        {
            byte r = buffer[i]; byte g = buffer[i + 1]; byte b = buffer[i + 2];
            if (Math.Abs(r - g) + Math.Abs(g - b) + Math.Abs(r - b) > 40 ||
                (r > 60 && g > 60 && b > 60)) return false;
        }
        return true;
    }

    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int ImageSize;
        public int XResolution;
        public int YResolution;
        public int ColorsUsed;
        public int ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
    }

    [DllImport("gdi32.dll")] private static extern bool GetDIBits(nint dc, nint bitmap, uint start, uint count, byte[] buffer, ref BitmapInfo info, uint colorUse);

    public static nint FindWindowForProcess(int pid, nint preferred = 0)
    {
        if (preferred != 0 && IsWindowVisible(preferred)) return preferred;
        nint result = 0;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var ownerPid);
            if (ownerPid == pid && IsWindowVisible(handle) && GetWindowTextLength(handle) > 0)
            {
                result = handle;
                return false;
            }
            return true;
        }, nint.Zero);
        return result;
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint handle);
    [DllImport("user32.dll", EntryPoint = "IsWindow")] private static extern bool IsWindowNative(nint handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint handle);
    [DllImport("user32.dll")] private static extern nint GetWindowDC(nint handle);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint handle, nint dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint handle, nint dc, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint objectHandle);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint objectHandle);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rasterOperation);
    private const uint SourceCopy = 0x00CC0020;
}
