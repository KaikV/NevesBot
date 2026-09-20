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
            if (!PrintWindow(handle, memoryDc, PrintWindowClientOnly) &&
                !BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0, SourceCopy))
                throw new InvalidOperationException("O cliente recusou a captura da janela.");
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
