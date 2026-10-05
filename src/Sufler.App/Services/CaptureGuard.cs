using System.Globalization;
using System.Runtime.InteropServices;
using Sufler.App.Interop;
using Sufler.Core.Capture;

namespace Sufler.App.Services;

/// <summary>
/// Keeps the prompt window out of screen captures and reports what the system actually
/// applied, read back from the window rather than assumed from the call's return value.
/// Single-threaded by design: the HWND belongs to the WPF UI thread, every member is meant to
/// be used there, and therefore nothing is guarded by a lock.
/// </summary>
public sealed class CaptureGuard : ICaptureGuard, IDisposable
{
    private IntPtr _window;

    public CaptureGuard()
    {
        _window = IntPtr.Zero;
        State = CaptureExclusionState.Visible;
        DiagnosticMessage = "Режим ещё не задан: окно пока видно в записи экрана.";
    }

    public CaptureExclusionState State { get; private set; }

    public string DiagnosticMessage { get; private set; }

    /// <summary>
    /// Binds the guard to the window handle whose capture affinity is managed.
    /// </summary>
    public void AttachWindow(IntPtr hwnd) => _window = hwnd;

    public CaptureExclusionState Apply(CaptureExclusionRequest request)
    {
        if (_window == IntPtr.Zero)
        {
            State = CaptureExclusionState.NotWorking;
            DiagnosticMessage = "Окно не привязано: AttachWindow не вызван, системе нечего передавать режим.";
            return State;
        }

        var requested = ToAffinity(request);
        var setOk = CaptureNative.SetWindowDisplayAffinity(_window, requested);
        var error = Marshal.GetLastWin32Error();

        var readBack = CaptureNative.WdaNone;
        var getOk = CaptureNative.GetWindowDisplayAffinity(_window, out readBack);
        State = getOk ? MapReadBack(readBack, request) : CaptureExclusionState.NotWorking;

        DiagnosticMessage = Describe(request, requested, setOk, error, getOk, readBack);
        return State;
    }

    public void Reset()
    {
        if (_window == IntPtr.Zero)
        {
            State = CaptureExclusionState.Visible;
            DiagnosticMessage = "Окно не привязано: сбрасывать нечего, окно и так не скрыто из записи.";
            return;
        }

        var setOk = CaptureNative.SetWindowDisplayAffinity(_window, CaptureNative.WdaNone);
        var error = Marshal.GetLastWin32Error();
        State = CaptureExclusionState.Visible;
        DiagnosticMessage = string.Create(
            CultureInfo.InvariantCulture,
            $"Сброс режима: SetWindowDisplayAffinity(WDA_NONE) вернул {setOk}, код Win32: {error}. Окно снова попадает в запись экрана.");
    }

    public void Dispose() => _window = IntPtr.Zero;

    private static uint ToAffinity(CaptureExclusionRequest request) => request switch
    {
        CaptureExclusionRequest.ExcludeFromCapture => CaptureNative.WdaExcludeFromCapture,
        CaptureExclusionRequest.MonitorOnly => CaptureNative.WdaMonitor,
        _ => CaptureNative.WdaNone,
    };

    private static CaptureExclusionState MapReadBack(uint readBack, CaptureExclusionRequest request)
    {
        if (readBack == CaptureNative.WdaExcludeFromCapture)
        {
            return CaptureExclusionState.ExcludedFromCapture;
        }

        if (readBack == CaptureNative.WdaMonitor)
        {
            return CaptureExclusionState.BlackBoxOnly;
        }

        return request == CaptureExclusionRequest.None ? CaptureExclusionState.Visible : CaptureExclusionState.NotWorking;
    }

    private static string DescribeRequest(CaptureExclusionRequest request) => request switch
    {
        CaptureExclusionRequest.ExcludeFromCapture => "ExcludeFromCapture — исключить окно из записи экрана",
        CaptureExclusionRequest.MonitorOnly => "MonitorOnly — WDA_MONITOR: в записи будет чёрный прямоугольник",
        _ => "None — окно видно в записи экрана",
    };

    private static string DescribeState(CaptureExclusionState state) => state switch
    {
        CaptureExclusionState.ExcludedFromCapture =>
            "окно исключено из записи экрана. Значение 0x11 означает, что система приняла запрос; фактическое отсутствие пикселей проверяется вручную, записью экрана.",
        CaptureExclusionState.BlackBoxOnly =>
            "система подставила WDA_MONITOR: в записи вместо окна будет чёрный прямоугольник. Настоящее исключение поддерживает только Windows 10 версии 2004 (сборка 19041) и новее.",
        CaptureExclusionState.Visible =>
            "окно видно в записи экрана.",
        _ =>
            "режим не действует: система не подтвердила запрос.",
    };

    private string Describe(
        CaptureExclusionRequest request,
        uint requested,
        bool setOk,
        int error,
        bool getOk,
        uint readBack) => string.Create(
        CultureInfo.InvariantCulture,
        $"""
        Запрошен режим: {request} — {DescribeRequest(request)} (флаг 0x{requested:X}).
        SetWindowDisplayAffinity вернул: {setOk}; код ошибки Win32: {error}.
        GetWindowDisplayAffinity вернул: {getOk}; прочитанный флаг окна: 0x{readBack:X}.
        Результат: {DescribeState(State)}
        """);
}