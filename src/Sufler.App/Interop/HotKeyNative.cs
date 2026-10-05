using System.Runtime.InteropServices;

namespace Sufler.App.Interop;

/// <summary>The global-hotkey surface of user32.</summary>
internal static class HotKeyNative
{
    internal const uint ModAlt = 0x1;
    internal const uint ModControl = 0x2;
    internal const uint ModShift = 0x4;
    internal const uint ModWin = 0x8;
    internal const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}