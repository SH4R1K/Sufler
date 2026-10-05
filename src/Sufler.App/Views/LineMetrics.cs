using System.Globalization;
using System.Windows;
using System.Windows.Media;

// System.Drawing arrives as an implicit using of this project, so the WPF types that share a
// simple name with it have to be pinned here.
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace Sufler.App.Views;

/// <summary>
/// Turns a font description into the line height the scroll engine needs. The engine
/// scrolls in pixels, so it has to know how many pixels one line of script occupies.
/// </summary>
internal static class LineMetrics
{
    /// <summary>
    /// Builds a typeface for <paramref name="fontFamily"/>, falling back to the UI font
    /// when the configured family is missing or has no glyphs at all.
    /// </summary>
    public static Typeface ResolveTypeface(string? fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            var candidate = new Typeface(new FontFamily(fontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (candidate.TryGetGlyphTypeface(out _))
            {
                return candidate;
            }
        }

        return new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }

    /// <summary>
    /// Height of a single line in device-independent pixels, or zero when the font size
    /// is unusable (callers must ignore a non-positive result).
    /// </summary>
    public static double MeasureLineHeight(Typeface typeface, double emSize, double pixelsPerDip)
    {
        if (typeface is null || double.IsNaN(emSize) || emSize <= 0d)
        {
            return 0d;
        }

        var probe = new FormattedText(
            "\u00A0",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            emSize,
            Brushes.Black,
            pixelsPerDip > 0d ? pixelsPerDip : 1d);

        return probe.Height;
    }
}