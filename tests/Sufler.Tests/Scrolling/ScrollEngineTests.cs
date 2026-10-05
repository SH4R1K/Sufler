using Sufler.Core.Scrolling;

namespace Sufler.Tests.Scrolling;

public sealed class ScrollEngineTests
{
    // 30 lines/min * 40 px / 60 s = 20 px/s; content 2000 px - viewport 400 px = 1600 px of travel.
    private const double LineHeight = 40d;
    private const double MaxOffset = 1600d;

    private static ScrollEngine CreateEngine(bool loop = false) => new()
    {
        LinesPerMinute = 30d,
        LineHeightPx = LineHeight,
        ViewportHeightPx = 400d,
        ContentHeightPx = 2000d,
        Loop = loop,
        LoopPauseSeconds = 2d,
    };

    [Fact]
    public void PixelsPerSecond_IsLinesPerMinuteTimesLineHeightPerMinute()
    {
        var engine = CreateEngine();

        Assert.Equal(20d, engine.PixelsPerSecond);

        engine.LinesPerMinute = 90d;
        engine.LineHeightPx = 12d;

        Assert.Equal(18d, engine.PixelsPerSecond);
    }

    [Fact]
    public void MaxOffsetPx_IsContentMinusViewportAndNeverNegative()
    {
        var engine = CreateEngine();

        Assert.Equal(MaxOffset, engine.MaxOffsetPx);

        engine.ContentHeightPx = 300d;

        Assert.Equal(0d, engine.MaxOffsetPx);
    }

    [Fact]
    public void NewEngine_StartsStoppedAtZero()
    {
        var engine = CreateEngine();

        Assert.False(engine.IsRunning);
        Assert.False(engine.IsPaused);
        Assert.False(engine.IsAtEnd);
        Assert.Equal(0d, engine.OffsetPx);
    }

    [Fact]
    public void Advance_WhenStopped_DoesNothing()
    {
        var engine = CreateEngine();

        var step = engine.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0d, step.OffsetPx);
        Assert.False(step.ReachedEnd);
        Assert.False(step.Looped);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void Advance_WhenRunning_MovesForward()
    {
        var engine = CreateEngine();
        engine.Start();

        var step = engine.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(200d, step.OffsetPx);
        Assert.False(step.ReachedEnd);
        Assert.False(step.Looped);
        Assert.True(engine.IsRunning);
    }

    [Fact]
    public void ToggleRunning_TogglesState()
    {
        var engine = CreateEngine();

        engine.ToggleRunning();
        Assert.True(engine.IsRunning);

        engine.ToggleRunning();
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void Stop_HaltsScrolling()
    {
        var engine = CreateEngine();
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(1));

        engine.Stop();
        var step = engine.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(20d, step.OffsetPx);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void Advance_ReachingEndStopsEngineAndReportsReachedEnd()
    {
        var engine = CreateEngine();
        engine.Start();

        var step = engine.Advance(TimeSpan.FromSeconds(100));

        Assert.Equal(MaxOffset, step.OffsetPx);
        Assert.True(step.ReachedEnd);
        Assert.False(engine.IsRunning);
        Assert.True(engine.IsAtEnd);
    }

    [Fact]
    public void Advance_AfterReachingEnd_StaysClampedAtEnd()
    {
        var engine = CreateEngine();
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(100));

        var step = engine.Advance(TimeSpan.FromSeconds(100));

        Assert.Equal(MaxOffset, step.OffsetPx);
        Assert.True(step.ReachedEnd);
        Assert.False(step.Looped);
        Assert.Equal(MaxOffset, engine.OffsetPx);
    }

    [Fact]
    public void Advance_WhenContentFitsViewport_ReportsReachedEndImmediately()
    {
        var engine = CreateEngine();
        engine.ContentHeightPx = 300d;
        engine.Start();

        var step = engine.Advance(TimeSpan.FromSeconds(1));

        Assert.True(step.ReachedEnd);
        Assert.Equal(0d, step.OffsetPx);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void Advance_WithZeroOrNegativeDelta_DoesNothing()
    {
        var engine = CreateEngine();
        engine.Start();

        var zero = engine.Advance(TimeSpan.Zero);
        var negative = engine.Advance(TimeSpan.FromSeconds(-30));

        Assert.Equal(0d, zero.OffsetPx);
        Assert.False(zero.ReachedEnd);
        Assert.Equal(0d, negative.OffsetPx);
        Assert.False(negative.ReachedEnd);
        Assert.True(engine.IsRunning);
    }

    [Fact]
    public void Advance_LoopMode_PausesAtEndWithoutStopping()
    {
        var engine = CreateEngine(loop: true);
        engine.Start();

        var step = engine.Advance(TimeSpan.FromSeconds(100));

        Assert.Equal(MaxOffset, step.OffsetPx);
        Assert.True(engine.IsPaused);
        Assert.False(step.Looped);
        Assert.True(engine.IsRunning);
    }

    [Fact]
    public void Advance_LoopMode_PauseIsNotConsumedEarly()
    {
        var engine = CreateEngine(loop: true);
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(100));

        var half = engine.Advance(TimeSpan.FromSeconds(1));
        var stillPaused = engine.Advance(TimeSpan.FromSeconds(0.5));

        Assert.Equal(MaxOffset, half.OffsetPx);
        Assert.False(half.Looped);
        Assert.True(engine.IsPaused);
        Assert.Equal(MaxOffset, stillPaused.OffsetPx);
        Assert.True(engine.IsPaused);
    }

    [Fact]
    public void Advance_LoopMode_WrapsToStartOncePauseElapsed()
    {
        var engine = CreateEngine(loop: true);
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(100));

        var wrapped = engine.Advance(TimeSpan.FromSeconds(2));

        Assert.True(wrapped.Looped);
        Assert.False(engine.IsPaused);
        Assert.Equal(0d, wrapped.OffsetPx);
        Assert.True(engine.IsRunning);
        Assert.False(engine.IsAtEnd);
    }

    [Fact]
    public void Advance_LoopMode_HugeDeltaUsesRemainingTimeAfterPause()
    {
        var engine = CreateEngine(loop: true);
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(100));

        var step = engine.Advance(TimeSpan.FromSeconds(10));

        Assert.True(step.Looped);
        Assert.False(engine.IsPaused);
        // 2 s of the delta end the pause, the remaining 8 s scroll forward from the top.
        Assert.Equal(160d, step.OffsetPx);
        Assert.False(step.ReachedEnd);
    }

    [Fact]
    public void Advance_LoopMode_HugeDeltaFromStartWrapsAtMostOnce()
    {
        var engine = CreateEngine(loop: true);
        engine.Start();

        var step = engine.Advance(TimeSpan.FromSeconds(1000));

        Assert.Equal(MaxOffset, step.OffsetPx);
        Assert.True(engine.IsPaused);
        Assert.False(step.Looped);
    }

    [Fact]
    public void ScrollLines_MovesByLineHeight()
    {
        var engine = CreateEngine();

        engine.ScrollLines(5);

        Assert.Equal(200d, engine.OffsetPx);
    }

    [Fact]
    public void ScrollLines_ClampsToBounds()
    {
        var engine = CreateEngine();

        engine.ScrollLines(1000);
        Assert.Equal(MaxOffset, engine.OffsetPx);

        engine.ScrollLines(-1000);
        Assert.Equal(0d, engine.OffsetPx);
    }

    [Fact]
    public void ScrollLines_DoesNotStopRunningEngineAtEnd()
    {
        var engine = CreateEngine();
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(1));

        engine.ScrollLines(1000);

        Assert.Equal(MaxOffset, engine.OffsetPx);
        Assert.True(engine.IsRunning);
    }

    [Fact]
    public void ScrollLines_ClearsLoopPause()
    {
        var engine = CreateEngine(loop: true);
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(100));
        Assert.True(engine.IsPaused);

        engine.ScrollLines(-1);

        Assert.False(engine.IsPaused);
        Assert.Equal(MaxOffset - LineHeight, engine.OffsetPx);
    }

    [Fact]
    public void PageBy_MovesByWholeViewports()
    {
        var engine = CreateEngine();

        engine.PageBy(2);

        // Viewport 400 px / line 40 px = 10 lines per page, 400 px per page.
        Assert.Equal(800d, engine.OffsetPx);

        engine.PageBy(-1);
        Assert.Equal(400d, engine.OffsetPx);
    }

    [Fact]
    public void PageBy_ClampsToBounds()
    {
        var engine = CreateEngine();

        engine.PageBy(50);
        Assert.Equal(MaxOffset, engine.OffsetPx);

        engine.PageBy(-50);
        Assert.Equal(0d, engine.OffsetPx);
    }

    [Fact]
    public void PageBy_UsesAtLeastOneLineWhenViewportIsSmallerThanLineHeight()
    {
        var engine = CreateEngine();
        engine.LineHeightPx = 400d;
        engine.ViewportHeightPx = 40d;

        engine.PageBy(1);

        Assert.Equal(400d, engine.OffsetPx);
    }

    [Fact]
    public void Reset_KeepsRunningState()
    {
        var engine = CreateEngine();
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(3));

        engine.Reset();

        Assert.Equal(0d, engine.OffsetPx);
        Assert.True(engine.IsRunning);
        Assert.False(engine.IsPaused);

        engine.Stop();
        engine.Advance(TimeSpan.FromSeconds(3));
        engine.Reset();

        Assert.Equal(0d, engine.OffsetPx);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void Reset_ClearsLoopPause()
    {
        var engine = CreateEngine(loop: true);
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(100));

        engine.Reset();

        Assert.False(engine.IsPaused);
        Assert.Equal(0d, engine.OffsetPx);
    }

    [Fact]
    public void OnContentChanged_ResetsToBeginning()
    {
        var engine = CreateEngine();
        engine.Start();
        engine.Advance(TimeSpan.FromSeconds(4));

        engine.OnContentChanged();

        Assert.Equal(0d, engine.OffsetPx);
    }

    [Fact]
    public void RemainingLines_CountsLinesLeftAtStartMiddleAndEnd()
    {
        var engine = CreateEngine();
        engine.Start();

        Assert.Equal(40, engine.RemainingLines);

        engine.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(30, engine.RemainingLines);

        engine.Advance(TimeSpan.FromSeconds(100));
        Assert.Equal(0, engine.RemainingLines);
        Assert.True(engine.IsAtEnd);
    }

    [Fact]
    public void RemainingLines_WithNonPositiveLineHeight_IsZeroAndDoesNotDivideByZero()
    {
        var engine = CreateEngine();
        engine.Start();

        engine.LineHeightPx = 0d;
        Assert.Equal(0, engine.RemainingLines);

        engine.LineHeightPx = -10d;
        Assert.Equal(0, engine.RemainingLines);

        var step = engine.Advance(TimeSpan.FromSeconds(5));
        Assert.False(double.IsNaN(step.OffsetPx));

        engine.ScrollLines(10);
        Assert.False(double.IsNaN(engine.OffsetPx));

        engine.PageBy(1);
        Assert.False(double.IsNaN(engine.OffsetPx));
    }

    [Fact]
    public void Advance_WithNonPositiveLineHeight_ProducesNoTravel()
    {
        var engine = CreateEngine();
        engine.LineHeightPx = 0d;
        engine.Start();

        var step = engine.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0d, step.OffsetPx);
        Assert.False(step.ReachedEnd);
    }
}