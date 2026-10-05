using Sufler.Core.HotKeys;

namespace Sufler.Tests.HotKeys;

public sealed class AppCommandsTests
{
    [Theory]
    [InlineData(AppCommand.ToggleCaptureExclusion, "Ctrl+Alt+H")]
    [InlineData(AppCommand.ToggleClickThrough, "Ctrl+Alt+C")]
    [InlineData(AppCommand.ToggleScrolling, "Ctrl+Alt+P")]
    [InlineData(AppCommand.ResetScroll, "Ctrl+Alt+R")]
    [InlineData(AppCommand.ToggleEditor, "Ctrl+Alt+T")]
    [InlineData(AppCommand.HideOrQuit, "Ctrl+Alt+Q")]
    public void DefaultGesture_MatchesDeclaredConstant(AppCommand command, string expected)
    {
        Assert.Equal(expected, AppCommands.DefaultGesture(command));
    }

    [Fact]
    public void DefaultGesture_ForUnknownCommand_ReturnsEmpty()
        => Assert.Equal(string.Empty, AppCommands.DefaultGesture((AppCommand)999));

    [Fact]
    public void All_ContainsEveryCommandInDeclarationOrder()
    {
        Assert.Equal(
            new[]
            {
                AppCommand.ToggleCaptureExclusion,
                AppCommand.ToggleClickThrough,
                AppCommand.ToggleScrolling,
                AppCommand.ResetScroll,
                AppCommand.ToggleEditor,
                AppCommand.HideOrQuit,
            },
            AppCommands.All);
    }

    [Fact]
    public void All_HasNonEmptyUniqueGesturePerCommand()
    {
        var gestures = AppCommands.All.Select(AppCommands.DefaultGesture).ToList();

        Assert.Equal(AppCommands.All.Count, gestures.Count);
        Assert.All(gestures, gesture => Assert.False(string.IsNullOrWhiteSpace(gesture)));
        Assert.Equal(gestures.Count, gestures.Distinct().Count());
    }
}