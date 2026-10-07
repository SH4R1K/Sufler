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

    private readonly ScrollEngine _engine = new();
    private readonly Stopwatch _clock = new();
    private readonly Debouncer _geometryChanged;
    private readonly DispatcherTimer _scrollTimer;

    private TimeSpan _lastTick;
    private double _lineHeightPx;
    private bool _metricsDirty = true;
    private bool _clickThrough;
    private bool _applyingSettings;

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

        UpdateStatusStrip();
    }

    public void ResetScroll()
    {
        _engine.Reset();
        ApplyOffset(_engine.OffsetPx);
        UpdateStatusStrip();
    }

    private void OnPrompterLoaded(object sender, RoutedEventArgs e)
    {
        _clock.Start();
        _lastTick = _clock.Elapsed;

        // The timer keeps ticking while the window is hidden on purpose: a paused timer would
        // collect one huge elapsed delta and make the script jump when the window comes back.
        _scrollTimer.Start();
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

    /// <summary>
    /// Applies the configured size and position. A size change arriving live from the editor
    /// must grow the prompter around its own center (new Left = Left + (oldW − newW)/2, same
    /// for Top) instead of only right/down, so the window stays visually anchored where the
    /// user put it; the shift is gated on a rendered size (ActualWidth/Height finite and &gt; 0),
    /// which leaves the first show from the constructor — nothing rendered yet — on the plain
    /// stored/fallback position with no movement at all.
    /// </summary>
    private void ApplyWindowGeometry(SuflerSettings settings)
    {
        var width = settings.WindowWidth is { } configuredWidth && configuredWidth > 0d ? configuredWidth : DefaultWidth;
        var height = settings.WindowHeight is { } configuredHeight && configuredHeight > 0d ? configuredHeight : DefaultHeight;

        // Half the size delta per axis: the window keeps its center, so the total growth is
        // exactly the configured one. Before the first layout the actual size is zero (or
        // non-finite) and there is no center to preserve, so the shift stays zero.
        var hasActualSize = ActualWidth > 0d && ActualHeight > 0d
            && double.IsFinite(ActualWidth) && double.IsFinite(ActualHeight);
        var shiftX = hasActualSize ? (ActualWidth - width) / 2d : 0d;
        var shiftY = hasActualSize ? (ActualHeight - height) / 2d : 0d;

        Width = width;
        Height = height;

        double left;
        double top;
        if (settings.WindowLeft is { } storedLeft &&
            settings.WindowTop is { } storedTop &&
            IsOnVirtualScreen(storedLeft, storedTop, width, height))
        {
            left = storedLeft;
            top = storedTop;
        }
        else
        {
            var centered = CenterInWorkArea(width, height);
            left = centered.X;
            top = centered.Y;
        }

        // Both positioning branches — restored from settings and centered on the work area —
        // shift by the same delta so the growth stays symmetric either way.
        left += shiftX;
        top += shiftY;

        if (!IsOnVirtualScreen(left, top, width, height))
        {
            // The symmetric shift may push the grown window off the virtual screen; fall back
            // to plain work-area centering so it always stays grabbable.
            var centered = CenterInWorkArea(width, height);
            left = centered.X;
            top = centered.Y;
        }

        Left = left;
        Top = top;

        if (hasActualSize)
        {
            // ApplySettings suppresses LocationChanged, so nothing else would push the shifted
            // position back before the debounced save runs on the caller-owned object; write it
            // here to keep that save from persisting the pre-shift one.
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
        }
    }

    /// <summary>
    /// Work-area centering as a pure calculation, shared by the initial placement and the
    /// on-screen fallback so both cannot drift apart.
    /// </summary>
    private static (double X, double Y) CenterInWorkArea(double width, double height)
    {
        var workArea = SystemParameters.WorkArea;
        return (
            workArea.Left + ((workArea.Width - width) / 2d),
            workArea.Top + ((workArea.Height - height) / 2d));
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
            return;
        }

        _metricsDirty = false;
        _engine.LineHeightPx = lineHeight;
        _engine.ContentHeightPx = contentHeight;
        _engine.ViewportHeightPx = viewportHeight;
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var elapsed = now - _lastTick;
        _lastTick = now;

        if (_engine.IsRunning)
        {
            // Speed comes from the measured elapsed time, never from counting timer ticks.
            var step = _engine.Advance(elapsed);
            ApplyOffset(step.OffsetPx);
        }

        UpdateStatusStrip();
    }

    private void ApplyOffset(double offsetPx)
    {
        // The clamp bound is normally the ScrollViewer's own ScrollableHeight, and the normal path is
        // that it is positive — the XAML asks for Hidden, not Disabled, precisely because "Disabled"
        // reports 0 forever (bars off, content measured to the viewport, extent == viewport). So this
        // is a fallback, not the mechanism: when the viewer says it cannot scroll but the engine has
        // measured content taller than the viewport, trust the engine's MaxOffsetPx so the offset is
        // not pinned to 0 before it ever reaches ScrollToVerticalOffset — which clamps to its own
        // range anyway, so no bogus offset can come out of it.
        var scrollable = Scroller.ScrollableHeight > 0d
            ? Scroller.ScrollableHeight
            : _engine.MaxOffsetPx;
        var target = Math.Clamp(offsetPx, 0d, scrollable);
        Scroller.ScrollToVerticalOffset(target);
    }

    private void OnPrompterMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // e.Delta keeps the Win32 WM_MOUSEWHEEL sign: positive for a notch up, negative for a
        // notch down (-120), which is also why a bare ScrollViewer decreases its offset on a
        // positive delta. A positive line count advances through the script, hence the negation.
        _engine.ScrollLines(-Math.Sign(e.Delta));
        e.Handled = true;
        ApplyOffset(_engine.OffsetPx);
        UpdateStatusStrip();
    }

    private void OnPrompterKeyDown(object sender, KeyEventArgs e)
    {
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
}