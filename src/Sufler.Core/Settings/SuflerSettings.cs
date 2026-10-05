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
    };

    private static double ClampFinite(double value, double fallback, double min, double max)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static double? SanitizePosition(double? value)
        => value is { } candidate && double.IsFinite(candidate) ? candidate : null;

    private static double? SanitizeSize(double? value)
        => value is { } candidate && double.IsFinite(candidate) && candidate > 0d ? candidate : null;
}