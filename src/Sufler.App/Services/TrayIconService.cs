using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Sufler.Core.HotKeys;
using Sufler.Core.Shell;

namespace Sufler.App.Services;

/// <summary>
/// Tray icon of the prompt: a Russian menu with the commands of <see cref="AppCommand"/>, the
/// saved scripts and the capture diagnostic. The icon itself comes from the system, so the
/// repository carries no binary assets.
/// </summary>
public sealed class TrayIconService : ITrayIcon
{
    private const int MaxToolTipLength = 63;
    private const int BalloonTimeoutMs = 4000;

    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly ToolStripMenuItem _scrollingItem;
    private readonly ToolStripMenuItem _scriptsItem;

    private string _toolTip = string.Empty;
    private bool _refreshing;
    private bool _disposed;

    /// <summary>
    /// The paths the «Скрипты» submenu was built from, or null while it holds no items yet. A
    /// refresh that reports the same files again leaves the items alone, which is what keeps the
    /// item whose <see cref="ToolStripMenuItem.Click"/> is being processed alive and usable.
    /// </summary>
    private string[]? _renderedScripts;

    public TrayIconService()
    {
        _menu = new ContextMenuStrip();

        AddAction("Сценарий…", () => EditorRequested?.Invoke(this, EventArgs.Empty));
        AddAction("Показать суфлёр", () => PrompterRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new ToolStripSeparator());
        _captureItem = AddToggle("Невидимый режим", AppCommand.ToggleCaptureExclusion);
        _clickThroughItem = AddToggle("Кликсквозь", AppCommand.ToggleClickThrough);
        _scrollingItem = AddToggle("Автопрокрутка", AppCommand.ToggleScrolling);
        AddCommand("В начало", AppCommand.ResetScroll);
        _menu.Items.Add(new ToolStripSeparator());
        _scriptsItem = new ToolStripMenuItem("Скрипты");
        _menu.Items.Add(_scriptsItem);
        _menu.Items.Add(new ToolStripSeparator());
        AddAction("Выход", () => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            ContextMenuStrip = _menu,
            Text = string.Empty,
            Visible = true,
        };
        _icon.DoubleClick += OnIconDoubleClick;

        Refresh();
    }

    /// <summary>
    /// Supplies the state shown by <see cref="Refresh"/>; a null provider is tolerated and
    /// leaves the menu without check marks.
    /// </summary>
    public Func<AppTrayState>? StateProvider { get; set; }

    public string ToolTip
    {
        get => _toolTip;
        set
        {
            _toolTip = value ?? string.Empty;
            ApplyToolTip();
        }
    }

    public event EventHandler? EditorRequested;

    /// <summary>
    /// Raised when the user asks for the prompt window to be put back on screen. It lives on the
    /// concrete class and not on <see cref="ITrayIcon"/> because the shell contract must not grow:
    /// only this application owns a prompt window that can be hidden.
    /// </summary>
    public event EventHandler? PrompterRequested;

    public event EventHandler<AppCommand>? CommandRequested;

    public event EventHandler? ExitRequested;

    /// <summary>
    /// Raised with the full path of a script picked from the «Скрипты» submenu.
    /// </summary>
    public event EventHandler<string>? ScriptRequested;

    public void Refresh()
    {
        if (_disposed || _refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var state = StateProvider?.Invoke();
            _captureItem.Checked = state?.CaptureExcluded == true;
            _clickThroughItem.Checked = state?.ClickThrough == true;
            _scrollingItem.Checked = state?.IsRunning == true;
            RebuildScripts(state?.Scripts);
            ApplyToolTip(state?.CaptureStatus);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Notify(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        _icon.ShowBalloonTip(BalloonTimeoutMs, title, message, ToolTipIcon.None);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.DoubleClick -= OnIconDoubleClick;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    /// <summary>
    /// Menu items raise their event when they are clicked, not when they are created, so the
    /// host may subscribe after the tray icon exists.
    /// </summary>
    private ToolStripMenuItem AddAction(string text, Action raise)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => raise();
        _menu.Items.Add(item);
        return item;
    }

    private ToolStripMenuItem AddCommand(string text, AppCommand command)
        => AddAction(text, () => CommandRequested?.Invoke(this, command));

    private ToolStripMenuItem AddToggle(string text, AppCommand command)
    {
        var item = AddCommand(text, command);
        item.CheckOnClick = false;
        return item;
    }

    /// <summary>
    /// Rebuilds the submenu, but only for a file set that is not on screen yet. The composition
    /// root refreshes the tray from the click handler of this very submenu, and clearing the items
    /// of a dropdown that is still raising its click disposes the item being clicked; comparing the
    /// files first means the common refresh leaves the items untouched, so no deferral through the
    /// message pump is needed and <see cref="Refresh"/> stays synchronous and thread-affine.
    /// </summary>
    private void RebuildScripts(IReadOnlyList<string>? scripts)
    {
        var wanted = scripts is null ? Array.Empty<string>() : scripts.ToArray();
        if (_renderedScripts is not null && _renderedScripts.SequenceEqual(wanted, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _renderedScripts = wanted;
        _scriptsItem.DropDownItems.Clear();
        if (wanted.Length == 0)
        {
            _scriptsItem.DropDownItems.Add(new ToolStripMenuItem("(нет)") { Enabled = false });
            return;
        }

        foreach (var path in wanted)
        {
            var item = new ToolStripMenuItem(Path.GetFileName(path))
            {
                Tag = path,
                ToolTipText = path,
            };
            item.Click += OnScriptClick;
            _scriptsItem.DropDownItems.Add(item);
        }
    }

    private void OnScriptClick(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem { Tag: string path })
        {
            ScriptRequested?.Invoke(this, path);
        }
    }

    private void OnIconDoubleClick(object? sender, EventArgs e) => EditorRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyToolTip() => ApplyToolTip(null);

    /// <summary>
    /// <see cref="NotifyIcon.Text"/> rejects strings longer than 63 characters, so the tooltip
    /// is cut before it is handed over.
    /// </summary>
    private void ApplyToolTip(string? captureStatus)
    {
        if (_disposed)
        {
            return;
        }

        var text = captureStatus is null
            ? _toolTip
            : string.IsNullOrWhiteSpace(captureStatus)
                ? _toolTip
                : string.IsNullOrWhiteSpace(_toolTip)
                    ? captureStatus
                    : $"{_toolTip} — {captureStatus}";

        _icon.Text = text.Length > MaxToolTipLength ? text[..MaxToolTipLength] : text;
    }
}