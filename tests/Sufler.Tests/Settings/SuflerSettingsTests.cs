using Sufler.Core.Settings;

namespace Sufler.Tests.Settings;

public sealed class SuflerSettingsTests
{
    [Theory]
    [InlineData(-50d, SuflerSettings.MinFontSize)]
    [InlineData(0d, SuflerSettings.MinFontSize)]
    [InlineData(11.9d, SuflerSettings.MinFontSize)]
    [InlineData(12d, 12d)]
    [InlineData(48d, 48d)]
    [InlineData(120d, 120d)]
    [InlineData(121d, SuflerSettings.MaxFontSize)]
    [InlineData(1000d, SuflerSettings.MaxFontSize)]
    public void Normalize_ClampsFontSize(double input, double expected)
    {
        var settings = new SuflerSettings { FontSize = input };

        settings.Normalize();

        Assert.Equal(expected, settings.FontSize);
    }

    [Theory]
    [InlineData(0d, SuflerSettings.MinLinesPerMinute)]
    [InlineData(4.9d, SuflerSettings.MinLinesPerMinute)]
    [InlineData(5d, 5d)]
    [InlineData(90d, 90d)]
    [InlineData(120d, 120d)]
    [InlineData(240d, SuflerSettings.MaxLinesPerMinute)]
    public void Normalize_ClampsLinesPerMinute(double input, double expected)
    {
        var settings = new SuflerSettings { LinesPerMinute = input };

        settings.Normalize();

        Assert.Equal(expected, settings.LinesPerMinute);
    }

    [Theory]
    [InlineData(0d, SuflerSettings.MinOpacity)]
    [InlineData(0.05d, SuflerSettings.MinOpacity)]
    [InlineData(0.1d, 0.1d)]
    [InlineData(0.85d, 0.85d)]
    [InlineData(1d, SuflerSettings.MaxOpacity)]
    [InlineData(1.5d, SuflerSettings.MaxOpacity)]
    public void Normalize_ClampsOpacity(double input, double expected)
    {
        var settings = new SuflerSettings { Opacity = input };

        settings.Normalize();

        Assert.Equal(expected, settings.Opacity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Normalize_ReplacesBlankFontFamilyWithDefault(string? fontFamily)
    {
        var settings = new SuflerSettings { FontFamily = fontFamily! };

        settings.Normalize();

        Assert.Equal("Segoe UI", settings.FontFamily);
    }

    [Fact]
    public void Normalize_KeepsCustomFontFamily()
    {
        var settings = new SuflerSettings { FontFamily = "Consolas" };

        settings.Normalize();

        Assert.Equal("Consolas", settings.FontFamily);
    }

    [Fact]
    public void Normalize_ReplacesNonFiniteWindowValuesWithNull()
    {
        var settings = new SuflerSettings
        {
            WindowLeft = double.NaN,
            WindowTop = double.PositiveInfinity,
            WindowWidth = double.NaN,
            WindowHeight = double.NegativeInfinity,
        };

        settings.Normalize();

        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowTop);
        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowHeight);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(-0.5d)]
    public void Normalize_RejectsNonPositiveWindowSize(double size)
    {
        var settings = new SuflerSettings { WindowWidth = size, WindowHeight = size };

        settings.Normalize();

        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowHeight);
    }

    [Fact]
    public void Normalize_KeepsPositiveWindowGeometryIncludingNegativePosition()
    {
        var settings = new SuflerSettings
        {
            WindowLeft = -1440d,
            WindowTop = -300d,
            WindowWidth = 800d,
            WindowHeight = 600.5d,
        };

        settings.Normalize();

        Assert.Equal(-1440d, settings.WindowLeft);
        Assert.Equal(-300d, settings.WindowTop);
        Assert.Equal(800d, settings.WindowWidth);
        Assert.Equal(600.5d, settings.WindowHeight);
    }

    [Fact]
    public void Normalize_DoesNotTouchFlags()
    {
        var settings = new SuflerSettings { Loop = true, CaptureExcluded = true, ClickThrough = true };

        settings.Normalize();

        Assert.True(settings.Loop);
        Assert.True(settings.CaptureExcluded);
        Assert.True(settings.ClickThrough);
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var settings = new SuflerSettings
        {
            FontFamily = "  ",
            FontSize = 999d,
            LinesPerMinute = -3d,
            Opacity = 42d,
            WindowLeft = double.NaN,
            WindowWidth = -20d,
        };

        settings.Normalize();
        var snapshot = settings.Clone();
        settings.Normalize();

        Assert.Equal("Segoe UI", settings.FontFamily);
        Assert.Equal(SuflerSettings.MaxFontSize, settings.FontSize);
        Assert.Equal(SuflerSettings.MinLinesPerMinute, settings.LinesPerMinute);
        Assert.Equal(SuflerSettings.MaxOpacity, settings.Opacity);
        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowWidth);
        Assert.Equal(snapshot.FontFamily, settings.FontFamily);
        Assert.Equal(snapshot.FontSize, settings.FontSize);
        Assert.Equal(snapshot.LinesPerMinute, settings.LinesPerMinute);
        Assert.Equal(snapshot.Opacity, settings.Opacity);
        Assert.Equal(snapshot.WindowTop, settings.WindowTop);
        Assert.Equal(snapshot.WindowHeight, settings.WindowHeight);
    }

    [Fact]
    public void Clone_ReturnsIndependentInstanceWithSameValues()
    {
        var original = new SuflerSettings
        {
            FontFamily = "Cascadia Mono",
            FontSize = 44d,
            LinesPerMinute = 72d,
            Opacity = 0.75d,
            Loop = true,
            CaptureExcluded = true,
            ClickThrough = true,
            WindowLeft = 10d,
            WindowTop = 20d,
            WindowWidth = 1024d,
            WindowHeight = 768d,
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.FontFamily, clone.FontFamily);
        Assert.Equal(original.FontSize, clone.FontSize);
        Assert.Equal(original.LinesPerMinute, clone.LinesPerMinute);
        Assert.Equal(original.Opacity, clone.Opacity);
        Assert.Equal(original.Loop, clone.Loop);
        Assert.Equal(original.CaptureExcluded, clone.CaptureExcluded);
        Assert.Equal(original.ClickThrough, clone.ClickThrough);
        Assert.Equal(original.WindowLeft, clone.WindowLeft);
        Assert.Equal(original.WindowTop, clone.WindowTop);
        Assert.Equal(original.WindowWidth, clone.WindowWidth);
        Assert.Equal(original.WindowHeight, clone.WindowHeight);

        clone.FontSize = 12d;
        clone.Loop = false;
        clone.WindowWidth = null;

        Assert.Equal(44d, original.FontSize);
        Assert.True(original.Loop);
        Assert.Equal(1024d, original.WindowWidth);
    }
}