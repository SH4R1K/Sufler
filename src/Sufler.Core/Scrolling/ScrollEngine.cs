namespace Sufler.Core.Scrolling;

/// <summary>
/// Outcome of a single <see cref="ScrollEngine.Advance"/> call.
/// </summary>
/// <param name="OffsetPx">Scroll offset after the step, in pixels.</param>
/// <param name="ReachedEnd">The step ended at the end of the content.</param>
/// <param name="Looped">The step wrapped from the end back to the top.</param>
public readonly record struct ScrollStep(double OffsetPx, bool ReachedEnd, bool Looped);

/// <summary>
/// Pure scrolling arithmetic: no timers, no IO, no UI. The host feeds
/// <see cref="Advance"/> with elapsed time and renders the resulting offset.
/// </summary>
public sealed class ScrollEngine
{
    private const double SecondsPerMinute = 60d;

    private double _pauseRemainingSeconds;

    public ScrollEngine()
    {
    }

    public double LinesPerMinute { get; set; } = 30d;
    public double LineHeightPx { get; set; } = 40d;
    public double ViewportHeightPx { get; set; } = 400d;
    public double ContentHeightPx { get; set; } = 2000d;
    public bool Loop { get; set; }
    public double LoopPauseSeconds { get; set; } = 2d;
    public double OffsetPx { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }

    public double PixelsPerSecond => LinesPerMinute * LineHeightPx / SecondsPerMinute;

    public double MaxOffsetPx => Math.Max(0d, ContentHeightPx - ViewportHeightPx);

    public bool IsAtEnd => OffsetPx >= MaxOffsetPx;

    public int RemainingLines
    {
        get
        {
            if (LineHeightPx <= 0d)
            {
                return 0;
            }

            var remaining = Math.Ceiling((MaxOffsetPx - OffsetPx) / LineHeightPx);
            if (remaining <= 0d)
            {
                return 0;
            }

            return remaining >= int.MaxValue ? int.MaxValue : (int)remaining;
        }
    }

    public void Start()
    {
        IsRunning = true;
        ClearPause();
    }

    public void Stop()
    {
        IsRunning = false;
        ClearPause();
    }

    public void ToggleRunning()
    {
        if (IsRunning)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    /// <summary>
    /// Moves back to the beginning and clears the loop pause without touching
    /// the running state.
    /// </summary>
    public void Reset()
    {
        OffsetPx = 0d;
        ClearPause();
    }

    public void OnContentChanged() => Reset();

    /// <summary>
    /// Moves the offset forward by the distance covered during
    /// <paramref name="delta"/>. A zero or negative delta is ignored.
    /// </summary>
    public ScrollStep Advance(TimeSpan delta)
    {
        if (!IsRunning)
        {
            return new ScrollStep(OffsetPx, IsAtEnd, false);
        }

        var seconds = delta.TotalSeconds;
        if (double.IsNaN(seconds) || seconds <= 0d)
        {
            return new ScrollStep(OffsetPx, IsAtEnd, false);
        }

        var looped = false;
        if (IsPaused)
        {
            var pauseStep = Math.Min(seconds, _pauseRemainingSeconds);
            _pauseRemainingSeconds -= pauseStep;
            seconds -= pauseStep;
            if (_pauseRemainingSeconds > 0d)
            {
                return new ScrollStep(OffsetPx, IsAtEnd, false);
            }

            ClearPause();
            OffsetPx = 0d;
            looped = true;
        }

        var travel = PixelsPerSecond * seconds;
        if (double.IsNaN(travel))
        {
            return new ScrollStep(OffsetPx, IsAtEnd, looped);
        }

        var next = OffsetPx + travel;
        if (next >= MaxOffsetPx)
        {
            OffsetPx = MaxOffsetPx;
            if (Loop)
            {
                BeginPause();
            }
            else
            {
                IsRunning = false;
            }

            return new ScrollStep(OffsetPx, true, looped);
        }

        OffsetPx = ClampOffset(next);
        return new ScrollStep(OffsetPx, false, looped);
    }

    /// <summary>
    /// Manual scrolling: transient, clamped and never stops the engine.
    /// </summary>
    public void ScrollLines(double lines)
    {
        if (double.IsNaN(lines))
        {
            return;
        }

        OffsetPx = ClampOffset(OffsetPx + (lines * LineHeightPx));
        ClearPause();
    }

    public void PageBy(int pages)
    {
        var lineHeight = LineHeightPx > 0d ? LineHeightPx : 0d;
        var linesPerPage = lineHeight > 0d ? Math.Max(1d, ViewportHeightPx / lineHeight) : 1d;

        OffsetPx = ClampOffset(OffsetPx + (pages * linesPerPage * lineHeight));
        ClearPause();
    }

    private void BeginPause()
    {
        IsPaused = true;
        _pauseRemainingSeconds = Math.Max(0d, LoopPauseSeconds);
    }

    private void ClearPause()
    {
        IsPaused = false;
        _pauseRemainingSeconds = 0d;
    }

    private double ClampOffset(double value)
    {
        if (double.IsNaN(value))
        {
            return OffsetPx;
        }

        var max = MaxOffsetPx;
        if (value <= 0d)
        {
            return 0d;
        }

        return value > max ? max : value;
    }
}