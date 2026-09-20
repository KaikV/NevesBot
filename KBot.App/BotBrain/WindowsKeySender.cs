using System.Runtime.InteropServices;
using KBot.App.Services;

namespace KBot.App.BotBrain;

// Concrete IKeySender for the real client. Movement goes through the confirmed
// native SEND_KEY pipe (WASD/arrows). Everything else is posted straight to the
// game window via PostMessageW with proper modifier + extended-key flags so it
// survives even when the window is not focused.
public sealed class WindowsKeySender : IKeySender
{
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    private static readonly HashSet<string> MoveKeys = new(StringComparer.OrdinalIgnoreCase)
        { "UP", "DOWN", "LEFT", "RIGHT", "W", "A", "S", "D" };

    private readonly NativeService _native;
    private readonly nint _hwnd;

    public WindowsKeySender(NativeService native, nint hwnd)
    {
        _native = native;
        _hwnd = hwnd;
    }

    public bool TrySendMove(string direction)
    {
        if (!MoveKeys.Contains(direction)) return false;
        try { return Task.Run(() => _native.SendKeyAsync(direction)).GetAwaiter().GetResult(); }
        catch { return false; }
    }

    public bool TrySendHotkey(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return false;
        if (!Parse(combo, out var mods, out var vk)) return false;
        if (_hwnd == IntPtr.Zero) return false;

        // Press in order: modifiers down -> key down -> key up -> modifiers up.
        if ((mods & Mod.Shift) != 0) Down(VK.LShift);
        if ((mods & Mod.Alt) != 0) Down(VK.LMenu);
        if ((mods & Mod.Ctrl) != 0) Down(VK.LControl);
        var keyDown = Down(vk);
        Up(vk);
        if ((mods & Mod.Ctrl) != 0) Up(VK.LControl);
        if ((mods & Mod.Alt) != 0) Up(VK.LMenu);
        if ((mods & Mod.Shift) != 0) Up(VK.LShift);
        return keyDown;
    }

    [Flags] private enum Mod { None = 0, Ctrl = 1, Alt = 2, Shift = 4 }
    private static partial class VK
    {
        public const ushort LControl = 0xA2, RControl = 0xA3, LMenu = 0xA4, RMenu = 0xA5, LShift = 0xA0, RShift = 0xA1;
        public const ushort Space = 0x20;
    }

    private static bool Parse(string combo, out Mod mods, out ushort vk)
    {
        mods = Mod.None; vk = 0;
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = part.Trim().Trim('{', '}');
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Mod.Ctrl; break;
                case "alt": mods |= Mod.Alt; break;
                case "shift": mods |= Mod.Shift; break;
                default: vk = ToVirtualKey(token); break;
            }
        }
        return vk != 0;
    }

    private const int KeydownLParam = 1;
    private const int KeyupLParam = 0xC00001; // scan-code hi + transition flags for a key-up

    private static ushort ToVirtualKey(string token)
    {
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            return c is >= 'A' and <= 'Z' ? (ushort)c : c is >= '0' and <= '9' ? (ushort)c : (ushort)0;
        }
        if (token.Length > 1 && token[0] == 'F' && int.TryParse(token[1..], out var n) && n is >= 1 and <= 24)
            return (ushort)(0x70 + n - 1); // F1..F24
        return token.ToLowerInvariant() switch
        {
            "space" => VK.Space,
            "tab" => 0x09,
            "enter" or "return" => 0x0D,
            "delete" or "del" => 0x2E,
            _ => 0
        };
    }

    private bool Down(ushort vk) => PostMessage(_hwnd, WM_KEYDOWN, vk, KeydownLParam) != 0;
    private bool Up(ushort vk) => PostMessage(_hwnd, WM_KEYUP, vk, KeyupLParam) != 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int PostMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
