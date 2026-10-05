using System.Runtime.InteropServices;

namespace Sufler.App.Interop;

/// <summary>
/// Extended-window-style manipulation for the prompter window: mouse click-through
/// and staying out of the Alt+Tab list. Excluding a window from screen capture is a
/// different API and lives elsewhere.
/// </summary>
public static class WindowStyleNative
{
    private const int GwlExstyle = -20;
    private const int WsexTransparent = 0x00000020;
    private const int WsexToolWindow = 0x00000080;

    /// <summary>
    /// Turns mouse click-through on or off. While it is on, Windows delivers mouse
    /// input to the window underneath, so the prompter gets no input at all.
    /// </summary>
    public static void SetClickThrough(IntPtr hwnd, bool enabled)
        => SetStyleBit(hwnd, WsexTransparent, enabled);

    /// <summary>
    /// Adds or removes <c>WS_EX_TOOLWINDOW</c>, which keeps the window out of Alt+Tab.
    /// </summary>
    public static void SetToolWindow(IntPtr hwnd, bool enabled)
        => SetStyleBit(hwnd, WsexToolWindow, enabled);

    private static void SetStyleBit(IntPtr hwnd, int bit, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var current = unchecked((int)GetWindowLongPtr(hwnd, GwlExstyle));
        var updated = enabled ? current | bit : current & ~bit;
        if (updated == current)
        {
            return;
        }

        SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(updated));
    }

    /// <summary>
    /// 64-bit builds export <c>GetWindowLongPtrW</c> while 32-bit builds only have
    /// <c>GetWindowLongW</c>; both are imported and picked by process bitness.
    /// </summary>
    private static long GetWindowLongPtr(IntPtr hwnd, int index)
        => IntPtr.Size == 8
            ? GetWindowLongPtrW(hwnd, index).ToInt64()
            : GetWindowLongW(hwnd, index);

    /// <summary>
    /// Returns the previous value of the requested style and never throws.
    /// </summary>
    private static long SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newStyle)
        => IntPtr.Size == 8
            ? SetWindowLongPtrW(hwnd, index, newStyle).ToInt64()
            : SetWindowLongW(hwnd, index, newStyle.ToInt32());

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr newStyle);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr hwnd, int index, int newStyle);
}