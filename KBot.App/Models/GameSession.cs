using System.Windows.Media.Imaging;
using System.Diagnostics;
using KBot.App.Services;

namespace KBot.App.Models;

public sealed class GameSession : IDisposable
{
    private readonly Process _process;
    private nint _windowHandle;

    internal GameSession(Process process, nint windowHandle, string executablePath)
    {
        _process = process;
        _windowHandle = windowHandle;
        ExecutablePath = executablePath;
    }

    public int Pid => _process.Id;
    public nint WindowHandle => _windowHandle;
    public string ExecutablePath { get; }
    public string WindowTitle => WindowCaptureService.GetWindowTitle(_windowHandle);
    public bool IsAlive
    {
        get
        {
            try { return !_process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public bool RefreshWindow()
    {
        if (!IsAlive) return false;
        if (WindowCaptureService.IsWindow(_windowHandle)) return true;
        _process.Refresh();
        _windowHandle = WindowCaptureService.FindWindowForProcess(_process.Id, _process.MainWindowHandle);
        return _windowHandle != 0;
    }

    public BitmapSource Capture() => RefreshWindow()
        ? WindowCaptureService.Capture(_windowHandle)
        : throw new InvalidOperationException("A janela do jogo não está disponível.");

    public void Dispose()
    {
        _process.Dispose();
    }
}
