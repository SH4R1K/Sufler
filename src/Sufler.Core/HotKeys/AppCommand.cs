namespace Sufler.Core.HotKeys;

public enum AppCommand
{
    ToggleCaptureExclusion,
    ToggleClickThrough,
    ToggleScrolling,
    ResetScroll,
    ToggleEditor,
    HideOrQuit,
}

/// <summary>
/// Commands reachable from global hotkeys and from the tray menu, with their
/// default keyboard gestures.
/// </summary>
public static class AppCommands
{
    public const string ToggleCaptureExclusionGesture = "Ctrl+Alt+H";
    public const string ToggleClickThroughGesture = "Ctrl+Alt+C";
    public const string ToggleScrollingGesture = "Ctrl+Alt+P";
    public const string ResetScrollGesture = "Ctrl+Alt+R";
    public const string ToggleEditorGesture = "Ctrl+Alt+T";
    public const string HideOrQuitGesture = "Ctrl+Alt+Q";

    private static readonly IReadOnlyList<AppCommand> AllCommands = Array.AsReadOnly(new[]
    {
        AppCommand.ToggleCaptureExclusion,
        AppCommand.ToggleClickThrough,
        AppCommand.ToggleScrolling,
        AppCommand.ResetScroll,
        AppCommand.ToggleEditor,
        AppCommand.HideOrQuit,
    });

    public static string DefaultGesture(AppCommand command) => command switch
    {
        AppCommand.ToggleCaptureExclusion => ToggleCaptureExclusionGesture,
        AppCommand.ToggleClickThrough => ToggleClickThroughGesture,
        AppCommand.ToggleScrolling => ToggleScrollingGesture,
        AppCommand.ResetScroll => ResetScrollGesture,
        AppCommand.ToggleEditor => ToggleEditorGesture,
        AppCommand.HideOrQuit => HideOrQuitGesture,
        _ => string.Empty,
    };

    public static IReadOnlyList<AppCommand> All => AllCommands;
}

/// <summary>
/// Registers the global hotkeys of <see cref="AppCommand"/> and reports which of
/// them the operating system accepted.
/// </summary>
public interface IHotKeyService : IDisposable
{
    IReadOnlyList<AppCommand> Registered { get; }

    IReadOnlyList<AppCommand> Unavailable { get; }

    string GestureFor(AppCommand command);

    bool TryRebind(AppCommand command, string gesture);

    event EventHandler<AppCommand>? CommandInvoked;
}