using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Sufler.App.Interop;
using Sufler.App.Views;
using Sufler.Core.Scrolling;
using Sufler.Core.Settings;

namespace Sufler.App;

/// <summary>
/// The prompter: a semi-transparent always-on-top window showing the script while it
/// scrolls. The window never persists anything itself; it reports geometry changes and
/// leaves storage to the composition root.
/// </summary>
public partial class MainWindow : Window
{
    private const double DefaultWidth = 720d;
    private const double DefaultHeight = 540d;

    private static readonly TimeSpan ScrollTickInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan GeometryChangedDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan DiagnosticInterval = TimeSpan.FromSeconds(1);

    private readonly ScrollEngine _engine = new();
    private readonly Stopwatch _clock = new();
    private readonly Debouncer _geometryChanged;
    private readonly DispatcherTimer _scrollTimer;

    private TimeSpan _lastTick;
    private double _lineHeightPx;
    private bool _metricsDirty = true;
    private bool _clickThrough;
    private bool _applyingSettings;

    // Scroll diagnostics (temporary, see ScrollDiagnostics). The timers below are the same clock as
    // the scroll tick, so a record never writes more than a line per second of real time; the rest
    // are the previous values of each gate, so a record is written the moment the thing it watches
    // changes even if the second has not turned yet.
    private TimeSpan _lastDiagnosticAt;
    private TimeSpan _lastApplyDiagnosticAt;
    private TimeSpan _lastMetricsDiagnosticAt;
    private double _tickCount;
    private double _lastAppliedTarget;
    private double _lastAppliedClampMax;
    private double _lastDiagnosticOffset;
    private bool _lastDiagnosticRunning;
    private bool _lastDiagnosticPaused;
    private double _lastRecordedApplyTarget = double.NaN;
    private double _lastLoggedLineHeight = double.NaN;
    private double _lastLoggedContentHeight = double.NaN;
    private double _lastLoggedViewportHeight = double.NaN;

    // Live reference to the settings the composition root owns: geometry is written back into
    // it before UserSettingChanged is raised. ApplySettings swaps it, so always write into the
    // instance handed over most recently.
    private SuflerSettings _settings;

    private string _speedText = string.Empty;
    private string _pixelsText = string.Empty;
    private string _stateText = string.Empty;

    public MainWindow(SuflerSettings settings, string scriptText)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        _settings = settings;

        _geometryChanged = new Debouncer(GeometryChangedDelay);
        _scrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = ScrollTickInterval };
        _scrollTimer.Tick += OnScrollTick;

        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        ContentRendered += OnContentRendered;
        Loaded += OnPrompterLoaded;
        Closed += OnPrompterClosed;
        LocationChanged += OnWindowGeometryChanged;
        SizeChanged += OnWindowGeometryChanged;
        LayoutUpdated += OnPrompterLayoutUpdated;
        IsVisibleChanged += OnPrompterIsVisibleChanged;
        PreviewMouseWheel += OnPrompterMouseWheel;
        PreviewKeyDown += OnPrompterKeyDown;

        Opacity = _settings.Opacity;
        ApplyScriptFont();
        ApplyEngineSettings();
        ApplyWindowGeometry(_settings);

        // Click-through is persisted, so it has to be seeded from the settings here as well:
        // the composition root creates the prompter once and never calls SetClickThrough
        // afterwards, which would leave the field, the native style and the tray out of sync
        // after a restart with the mode enabled.
        SetClickThrough(_settings.ClickThrough);
        SetScriptText(scriptText);
    }

    /// <summary>
    /// The window position or size changed and may have settled; the composition root can
    /// persist the current geometry of <see cref="SuflerSettings"/>.
    /// </summary>
    public event EventHandler? UserSettingChanged;

    public bool ClickThrough => _clickThrough;

    public bool IsScrollingRunning => _engine.IsRunning;

    /// <summary>
    /// Copies font, playback and geometry values from the settings and re-applies them.
    /// The reference is kept so later geometry writes land in the object the caller owns.
    /// </summary>
    public void ApplySettings(SuflerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        _settings = settings;

        _applyingSettings = true;
        try
        {
            Opacity = _settings.Opacity;
            ApplyScriptFont();
            ApplyEngineSettings();
            ApplyWindowGeometry(_settings);
            SetClickThrough(_settings.ClickThrough);
        }
        finally
        {
            _applyingSettings = false;
        }

        PushMetrics();
        ApplyNativeStyles();
        UpdateStatusStrip();
    }

    /// <summary>
    /// Replaces the script and rewinds the engine, which resets the offset and any loop pause.
    /// </summary>
    public void SetScriptText(string text)
    {
        ScriptTextBlock.Text = text ?? string.Empty;
        EmptyHint.Visibility = string.IsNullOrWhiteSpace(ScriptTextBlock.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        InvalidateMetrics();
        _engine.OnContentChanged();
        ApplyOffset(_engine.OffsetPx);
        UpdateStatusStrip();
    }

    /// <summary>
    /// In click-through mode Windows routes every mouse message to the window underneath, so
    /// the prompter deliberately receives no input: no dragging, no wheel scrolling, no text
    /// selection. The global hotkey is the way in and out of the mode.
    /// </summary>
    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        ApplyNativeStyles();
        ScrollDiagnostics.Log("click-through", DiagnosticSnapshot(_lastAppliedTarget, _lastAppliedClampMax));
    }

    public void SetScrollingRunning(bool running)
    {
        if (running)
        {
            _engine.Start();
        }
        else
        {
            _engine.Stop();
        }

        ScrollDiagnostics.Log("run-toggle", DiagnosticSnapshot(_lastAppliedTarget, _lastAppliedClampMax));
        UpdateStatusStrip();
    }

    public void ResetScroll()
    {
        _engine.Reset();
        ApplyOffset(_engine.OffsetPx);
        ScrollDiagnostics.Log("reset", DiagnosticSnapshot(_lastAppliedTarget, _lastAppliedClampMax));
        UpdateStatusStrip();
    }

    private void OnPrompterLoaded(object sender, RoutedEventArgs e)
    {
        _clock.Start();
        _lastTick = _clock.Elapsed;

        // The timer keeps ticking while the window is hidden on purpose: a paused timer would
        // collect one huge elapsed delta and make the script jump when the window comes back.
        _scrollTimer.Start();

        ScrollDiagnostics.Log("loaded", DiagnosticSnapshot(_lastAppliedTarget, _lastAppliedClampMax));
    }

    private void OnPrompterClosed(object? sender, EventArgs e)
    {
        _scrollTimer.Stop();
        _scrollTimer.Tick -= OnScrollTick;
        _geometryChanged.Flush();
        _geometryChanged.Dispose();
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyNativeStyles();

    /// <summary>
    /// WPF drops the tool-window and click-through bits on its own while the window is hidden
    /// and rewrites them on the next Show, so the styles have to be re-applied on the way back:
    /// otherwise a prompter shown again can pop up in Alt+Tab or start swallowing clicks while
    /// the tray still reports click-through as enabled.
    /// </summary>
    private void OnPrompterIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ScrollDiagnostics.Log("visible", DiagnosticSnapshot(_lastAppliedTarget, _lastAppliedClampMax));

        if (IsVisible)
        {
            ApplyNativeStyles();
        }
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ApplyNativeStyles();
        InvalidateMetrics();
        PushMetrics();
        _lastTick = _clock.Elapsed;
    }

    private void ApplyNativeStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // WPF rewrites the extended window style whenever its own window state changes
        // (activation, Show/Hide, visibility of a layered window), which silently drops the
        // bits set here. That is why these calls are repeated on SourceInitialized,
        // ContentRendered and after every toggle instead of being done once in Loaded.
        WindowStyleNative.SetToolWindow(hwnd, true);
        WindowStyleNative.SetClickThrough(hwnd, _clickThrough);
    }

    private void ApplyWindowGeometry(SuflerSettings settings)
    {
        var width = settings.WindowWidth is { } configuredWidth && configuredWidth > 0d ? configuredWidth : DefaultWidth;
        var height = settings.WindowHeight is { } configuredHeight && configuredHeight > 0d ? configuredHeight : DefaultHeight;

        Width = width;
        Height = height;

        if (settings.WindowLeft is { } left &&
            settings.WindowTop is { } top &&
            IsOnVirtualScreen(left, top, width, height))
        {
            Left = left;
            Top = top;
            return;
        }

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + ((workArea.Width - width) / 2d);
        Top = workArea.Top + ((workArea.Height - height) / 2d);
    }

    /// <summary>
    /// A stored position can point at a monitor that no longer exists, which would restore the
    /// prompter outside the visible desktop with no way back short of editing settings.json.
    /// Only a rectangle overlapping the current virtual screen by a usable amount is accepted.
    /// </summary>
    private static bool IsOnVirtualScreen(double left, double top, double width, double height)
    {
        var screen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        if (screen.IsEmpty || screen.Width <= 0d || screen.Height <= 0d)
        {
            return false;
        }

        var candidate = new Rect(left, top, width, height);
        if (!candidate.IntersectsWith(screen))
        {
            return false;
        }

        // A single pixel left on screen is not enough to grab the window back by hand, so a
        // tenth of it has to stay inside the desktop.
        var required = Math.Min(screen.Width, screen.Height) / 10d;
        var visibleWidth = Math.Min(screen.Right, candidate.Right) - Math.Max(screen.Left, candidate.Left);
        var visibleHeight = Math.Min(screen.Bottom, candidate.Bottom) - Math.Max(screen.Top, candidate.Top);
        return visibleWidth >= required || visibleHeight >= required;
    }

    private void OnWindowGeometryChanged(object? sender, EventArgs e)
    {
        if (_applyingSettings || !IsLoaded)
        {
            return;
        }

        _geometryChanged.Request(RaiseUserSettingChanged);
    }

    private void RaiseUserSettingChanged()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top))
        {
            return;
        }

        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.WindowWidth = ActualWidth > 0d ? ActualWidth : Width;
        _settings.WindowHeight = ActualHeight > 0d ? ActualHeight : Height;
        UserSettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyEngineSettings()
    {
        _engine.LinesPerMinute = _settings.LinesPerMinute;
        _engine.Loop = _settings.Loop;
    }

    private void ApplyScriptFont()
    {
        var typeface = LineMetrics.ResolveTypeface(_settings.FontFamily);
        ScriptTextBlock.FontFamily = typeface.FontFamily;
        ScriptTextBlock.FontSize = _settings.FontSize;
        _lineHeightPx = LineMetrics.MeasureLineHeight(
            typeface,
            _settings.FontSize,
            VisualTreeHelper.GetDpi(ScriptTextBlock).PixelsPerDip);
        InvalidateMetrics();
    }

    private void OnPrompterLayoutUpdated(object? sender, EventArgs e)
    {
        InvalidateMetrics();
        PushMetrics();
    }

    private void InvalidateMetrics() => _metricsDirty = true;

    /// <summary>
    /// Feeds the measured text metrics to the engine. A non-positive line height, content
    /// height or viewport height means nothing is laid out yet, so the engine keeps the
    /// metrics it already had.
    /// </summary>
    private void PushMetrics()
    {
        if (!_metricsDirty)
        {
            return;
        }

        var lineHeight = _lineHeightPx;
        var contentHeight = ScriptTextBlock.ActualHeight;
        var viewportHeight = Scroller.ActualHeight;
        if (lineHeight <= 0d || contentHeight <= 0d || viewportHeight <= 0d)
        {
            // LayoutUpdated raises this on every layout pass, and a refusal leaves the metrics dirty
            // on purpose, so the reason the engine is not being fed is exactly the thing worth
            // having in the log: which of the three measured values was not there yet.
            LogMetricsDiagnostic(
                "metrics-skip",
                lineHeight,
                contentHeight,
                viewportHeight,
                lineHeight <= 0d ? "lineHeight" : contentHeight <= 0d ? "contentHeight" : "viewportHeight");
            return;
        }

        _metricsDirty = false;
        _engine.LineHeightPx = lineHeight;
        _engine.ContentHeightPx = contentHeight;
        _engine.ViewportHeightPx = viewportHeight;

        LogMetricsDiagnostic("metrics", lineHeight, contentHeight, viewportHeight, "none");
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var elapsed = now - _lastTick;
        _lastTick = now;
        _tickCount += 1d;

        if (_engine.IsRunning)
        {
            // Speed comes from the measured elapsed time, never from counting timer ticks.
            var step = _engine.Advance(elapsed);
            ApplyOffset(step.OffsetPx);
        }

        LogTickDiagnostic(elapsed);
        UpdateStatusStrip();
    }

    private void ApplyOffset(double offsetPx)
    {
        var scrollable = Math.Max(0d, Scroller.ScrollableHeight);
        var target = Math.Clamp(offsetPx, 0d, scrollable);
        Scroller.ScrollToVerticalOffset(target);

        _lastAppliedTarget = target;
        _lastAppliedClampMax = scrollable;

        // Recording the target is what separates "the engine moves the offset and the ScrollViewer
        // ignores it" from "the engine does not move it at all": both look identical in the
        // status strip, only this line tells them apart.
        var moved = double.IsNaN(target) || Math.Abs(target - _lastRecordedApplyTarget) > 1d;
        if (moved || _clock.Elapsed - _lastApplyDiagnosticAt >= DiagnosticInterval)
        {
            _lastApplyDiagnosticAt = _clock.Elapsed;
            _lastRecordedApplyTarget = target;
            ScrollDiagnostics.Log("apply", DiagnosticSnapshot(target, scrollable));
        }
    }

    private void OnPrompterMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // e.Delta keeps the Win32 WM_MOUSEWHEEL sign: positive for a notch up, negative for a
        // notch down (-120), which is also why a bare ScrollViewer decreases its offset on a
        // positive delta. A positive line count advances through the script, hence the negation.
        _engine.ScrollLines(-Math.Sign(e.Delta));
        e.Handled = true;
        ApplyOffset(_engine.OffsetPx);
        ScrollDiagnostics.Log(
            "wheel",
            DiagnosticSnapshot(_lastAppliedTarget, _lastAppliedClampMax, inputDelta: e.Delta));
        UpdateStatusStrip();
    }

    private void OnPrompterKeyDown(object sender, KeyEventArgs e)
    {
        // Logged before anything else: a prompter that never records a key is not receiving input at
        // all, and that is a different fault from one that receives it and does not scroll.
        ScrollDiagnostics.Log(
            $"key:{e.Key}",
            DiagnosticSnapshot(
                _lastAppliedTarget,
                _lastAppliedClampMax,
                modifiers: (int)Keyboard.Modifiers));

        // Modified combinations belong to the application and to the global hotkeys; leaving
        // them unhandled is what keeps Ctrl+V and Ctrl+Alt+T working.
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                _engine.ScrollLines(-1d);
                break;
            case Key.Down:
                _engine.ScrollLines(1d);
                break;
            case Key.PageUp:
                _engine.PageBy(-1);
                break;
            case Key.PageDown:
                _engine.PageBy(1);
                break;
            case Key.Home:
                ResetScroll();
                e.Handled = true;
                return;
            default:
                return;
        }

        e.Handled = true;
        ApplyOffset(_engine.OffsetPx);
        UpdateStatusStrip();
    }

    private void OnDragStripMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnPrompterMouseEnter(object sender, MouseEventArgs e) => StatusStrip.Opacity = 1d;

    private void OnPrompterMouseLeave(object sender, MouseEventArgs e) => StatusStrip.Opacity = 0.45d;

    private void UpdateStatusStrip()
    {
        var speed = $"{_engine.LinesPerMinute:0} строк/мин";
        if (!string.Equals(speed, _speedText, StringComparison.Ordinal))
        {
            _speedText = speed;
            SpeedText.Text = speed;
        }

        var pixels = $"≈ {_engine.PixelsPerSecond:0} px/с";
        if (!string.Equals(pixels, _pixelsText, StringComparison.Ordinal))
        {
            _pixelsText = pixels;
            PixelsText.Text = pixels;
        }

        var state = DescribeRunState();
        if (!string.Equals(state, _stateText, StringComparison.Ordinal))
        {
            _stateText = state;
            StateText.Text = state;
        }
    }

    private string DescribeRunState()
    {
        if (_engine.MaxOffsetPx <= 0d)
        {
            return "Стоп";
        }

        if (_engine.IsPaused)
        {
            return "Пауза";
        }

        if (_engine.IsRunning)
        {
            return "Идёт";
        }

        return _engine.IsAtEnd ? "В конце" : "Стоп";
    }

    // ---------------------------------------------------------------------------
    // Temporary scroll diagnostics. Read only, no behaviour: every method below either returns
    // without touching the scroll path or appends a line to the diagnostics file. Delete the block
    // and the ScrollDiagnostics.Log calls once the "does not scroll" report is closed.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Records a tick. Once a second while nothing changes, and immediately whenever the run state
    /// or the offset moves: the offset is compared with the same one pixel tolerance the apply record
    /// uses, because a live scroll moves it on every single tick.
    /// </summary>
    private void LogTickDiagnostic(TimeSpan elapsed)
    {
        var running = _engine.IsRunning;
        var paused = _engine.IsPaused;
        var offset = _engine.OffsetPx;

        var changed = running != _lastDiagnosticRunning
            || paused != _lastDiagnosticPaused
            || double.IsNaN(_lastDiagnosticOffset)
            || Math.Abs(offset - _lastDiagnosticOffset) > 1d;
        if (!changed && _clock.Elapsed - _lastDiagnosticAt < DiagnosticInterval)
        {
            return;
        }

        _lastDiagnosticAt = _clock.Elapsed;
        _lastDiagnosticRunning = running;
        _lastDiagnosticPaused = paused;
        _lastDiagnosticOffset = offset;

        ScrollDiagnostics.Log(
            "tick",
            DiagnosticSnapshot(_lastAppliedTarget, _lastAppliedClampMax, deltaMs: elapsed.TotalMilliseconds));
    }

    /// <summary>
    /// Records a metrics push, either the one the engine accepted or the one a half-finished layout
    /// made impossible. Both happen on every layout pass, so the measured triple is logged whenever
    /// it changes and otherwise at most once a second.
    /// </summary>
    private void LogMetricsDiagnostic(
        string reason,
        double lineHeight,
        double contentHeight,
        double viewportHeight,
        string skipCause)
    {
        var changed = lineHeight != _lastLoggedLineHeight
            || contentHeight != _lastLoggedContentHeight
            || viewportHeight != _lastLoggedViewportHeight;
        if (!changed && _clock.Elapsed - _lastMetricsDiagnosticAt < DiagnosticInterval)
        {
            return;
        }

        _lastMetricsDiagnosticAt = _clock.Elapsed;
        _lastLoggedLineHeight = lineHeight;
        _lastLoggedContentHeight = contentHeight;
        _lastLoggedViewportHeight = viewportHeight;

        ScrollDiagnostics.Log(
            reason,
            DiagnosticSnapshot(_lastAppliedTarget, _lastAppliedClampMax, skipCause: skipCause));
    }

    private ScrollDiagnosticsSnapshot DiagnosticSnapshot(
        double target,
        double clampMax,
        double deltaMs = 0d,
        double inputDelta = 0d,
        double modifiers = 0d,
        string skipCause = "-") =>
        new()
        {
            Ticks = _tickCount,
            Running = _engine.IsRunning,
            Paused = _engine.IsPaused,
            Offset = _engine.OffsetPx,
            Max = _engine.MaxOffsetPx,
            LineHeight = _engine.LineHeightPx,
            Content = _engine.ContentHeightPx,
            ViewportEngine = _engine.ViewportHeightPx,
            Scrollable = Scroller.ScrollableHeight,
            ViewportReal = Scroller.ViewportHeight,
            Extent = Scroller.ExtentHeight,
            VerticalOffset = Scroller.VerticalOffset,
            Target = target,
            ClampMax = clampMax,
            TextLength = ScriptTextBlock.Text.Length,
            HintVisible = EmptyHint.Visibility == Visibility.Visible,
            DeltaMs = deltaMs,
            ClickThrough = _clickThrough,
            RawLineHeight = _lineHeightPx,
            TextActualHeight = ScriptTextBlock.ActualHeight,
            ScrollerActualHeight = Scroller.ActualHeight,
            InputDelta = inputDelta,
            Modifiers = modifiers,
            SkipCause = skipCause,
        };
}