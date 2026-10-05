using System.Windows.Threading;

namespace Sufler.App.Views;

/// <summary>
/// Coalesces a burst of requests into one delayed callback on the WPF dispatcher.
/// </summary>
internal sealed class Debouncer : IDisposable
{
    private readonly DispatcherTimer _timer;
    private Action? _pending;

    public Debouncer(TimeSpan delay)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// Schedules <paramref name="action"/>, restarting the delay if one is already pending.
    /// </summary>
    public void Request(Action action)
    {
        _pending = action;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>
    /// Runs a pending callback right away. Does nothing when nothing is pending.
    /// </summary>
    public void Flush()
    {
        if (_pending is null)
        {
            return;
        }

        _timer.Stop();
        var pending = _pending;
        _pending = null;
        pending();
    }

    public void Dispose()
    {
        _timer.Stop();
        _pending = null;
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        var pending = _pending;
        _pending = null;
        pending?.Invoke();
    }
}