using Sufler.Core.HotKeys;

namespace Sufler.Core.Settings;

/// <summary>
/// User-visible configuration of the teleprompter window and playback.
/// </summary>
public sealed class SuflerSettings
{
    public const double MinLinesPerMinute = 5d;
    public const double MaxLinesPerMinute = 120d;
    public const double DefaultLinesPerMinute = 30d;
    public const double MinOpacity = 0.1d;
    public const double MaxOpacity = 1.0d;
    public const double MinFontSize = 12d;
    public const double MaxFontSize = 120d;

    private const double DefaultFontSize = 36d;
    private const double DefaultOpacity = 0.6d;
    private const int MaxHotKeyEntries = 6;

    private static readonly HashSet<string> CommandNames =
        new(AppCommands.All.Select(command => command.ToString()), StringComparer.Ordinal);

    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 36d;
    public double LinesPerMinute { get; set; } = DefaultLinesPerMinute;
    public double Opacity { get; set; } = 0.6d;
    public bool Loop { get; set; }
    public bool CaptureExcluded { get; set; }
    public bool ClickThrough { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    /// <summary>
    /// Persisted hot-key gestures: command name -> gesture, e.g. "ResetScroll" -> "Ctrl+Alt+Alt+R".
    /// Null means "use the defaults from AppCommands".
    /// </summary>
    public Dictionary<string, string>? HotKeys { get; set; }

    /// <summary>
    /// Brings every value into its supported range. Safe to call repeatedly: the
    /// result of a call is a fixed point of the method.
    /// </summary>
    public void Normalize()
    {
        FontSize = ClampFinite(FontSize, DefaultFontSize, MinFontSize, MaxFontSize);
        LinesPerMinute = ClampFinite(LinesPerMinute, DefaultLinesPerMinute, MinLinesPerMinute, MaxLinesPerMinute);
        Opacity = ClampFinite(Opacity, DefaultOpacity, MinOpacity, MaxOpacity);

        if (string.IsNullOrWhiteSpace(FontFamily))
        {
            FontFamily = "Segoe UI";
        }

        WindowLeft = SanitizePosition(WindowLeft);
        WindowTop = SanitizePosition(WindowTop);
        WindowWidth = SanitizeSize(WindowWidth);
        WindowHeight = SanitizeSize(WindowHeight);
        HotKeys = NormalizeHotKeys(HotKeys);
    }

    public SuflerSettings Clone() => new()
    {
        FontFamily = FontFamily,
        FontSize = FontSize,
        LinesPerMinute = LinesPerMinute,
        Opacity = Opacity,
        Loop = Loop,
        CaptureExcluded = CaptureExcluded,
        ClickThrough = ClickThrough,
        WindowLeft = WindowLeft,
        WindowTop = WindowTop,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        HotKeys = HotKeys is null ? null : new Dictionary<string, string>(HotKeys, StringComparer.Ordinal),
    };

    private static double ClampFinite(double value, double fallback, double min, double max)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static double? SanitizePosition(double? value)
        => value is { } candidate && double.IsFinite(candidate) ? candidate : null;

    private static double? SanitizeSize(double? value)
        => value is { } candidate && double.IsFinite(candidate) && candidate > 0d ? candidate : null;

    /// <summary>
    /// Keeps only entries the application can act on: a known command name and a non-blank
    /// gesture, at most one per command. Null stays null, meaning "use the defaults". The
    /// surviving order is the order of the incoming dictionary, so a second call finds nothing
    /// left to change.
    /// </summary>
    private static Dictionary<string, string>? NormalizeHotKeys(Dictionary<string, string>? hotKeys)
    {
        if (hotKeys is null)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (command, gesture) in hotKeys)
        {
            if (normalized.Count >= MaxHotKeyEntries)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(gesture))
            {
                continue;
            }

            if (CommandNames.Contains(command))
            {
                normalized[command] = gesture;
            }
        }

        return normalized;
    }
}