using Sufler.Core.HotKeys;

namespace Sufler.Core.Shell;

/// <summary>
/// Tray icon surface: menu actions, tooltip and balloon notifications.
/// </summary>
public interface ITrayIcon : IDisposable
{
    string ToolTip { get; set; }

    event EventHandler? EditorRequested;

    event EventHandler<AppCommand>? CommandRequested;

    event EventHandler? ExitRequested;

    void Refresh();

    void Notify(string title, string message);
}