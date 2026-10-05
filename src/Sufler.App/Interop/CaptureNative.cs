using System.Runtime.InteropServices;

namespace Sufler.App.Interop;

/// <summary>
/// The display-affinity surface of user32. Affinity 0x11 only means the system accepted the
/// request; whether the pixels are really absent from a capture is confirmed by recording the
/// screen, never by the return value.
/// </summary>
internal static class CaptureNative
{
    internal const uint WdaNone = 0x0;
    internal const uint WdaMonitor = 0x1;
    internal const uint WdaExcludeFromCapture = 0x11;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint pdwAffinity);
}