using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Sufler.App.Services;
using Sufler.App.Views;
using Sufler.Core.Capture;
using Sufler.Core.HotKeys;
using Sufler.Core.Script;
using Sufler.Core.Settings;

namespace Sufler.App;

/// <summary>
/// Composition root. It owns the stores, the capture guard, the hotkeys and the tray icon, and it
/// is the only place that writes to disk: both windows report what the user did, this class
/// persists it and keeps the tray and the editor in sync.
/// </summary>
public partial class App : Application
{
    private const string WindowName = "Суфлёр";
    private const string ToolTipText = "Суфлёр";

    private static readonly TimeSpan ScriptSaveDelay = TimeSpan.FromSeconds(1);

    private JsonSettingsStore? _settingsStore;
    private FileScriptStore? _scriptStore;
    private CaptureGuard? _capture;
    private TrayIconService? _tray;
    private GlobalHotKeyService? _hotKeys;
    private Debouncer? _scriptSave;

    private MainWindow? _prompter;
    private EditorWindow? _editor;
    private SuflerSettings? _settings;

    private string _scriptText = string.Empty;
    private string _captureStatus = string.Empty;
    private string? _currentScriptPath;
    private bool _windowAttached;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        SessionEnding += OnSessionEnding;

        _settingsStore = new JsonSettingsStore();
        _scriptStore = new FileScriptStore();
        _capture = new CaptureGuard();
        _tray = new TrayIconService();
        _scriptSave = new Debouncer(ScriptSaveDelay);

        // One live settings instance for the whole run: the prompter writes the window geometry
        // into the object it was given, so replacing it silently would drop those writes.
        var settings = _settingsStore.Load();
        settings.Normalize();
        _settings = settings;

        _scriptText = ScriptText.Normalize(_scriptStore.LoadCurrentText());

        _tray.StateProvider = BuildTrayState;
        _tray.EditorRequested += OnEditorRequested;
        _tray.PrompterRequested += OnPrompterRequested;
        _tray.CommandRequested += OnCommandRequested;
        _tray.ScriptRequested += OnScriptRequested;
        _tray.ExitRequested += OnExitRequested;

        // The prompter is the only window opened here. The editor is a control panel that has to
        // stay out of the way until it is asked for.
        _prompter = new MainWindow(settings, _scriptText);
        _prompter.SourceInitialized += OnPrompterSourceInitialized;
        _prompter.ContentRendered += OnPrompterContentRendered;
        _prompter.UserSettingChanged += OnPrompterUserSettingChanged;
        _prompter.Closing += OnPrompterClosing;
        _prompter.Show();

        RefreshTray();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // The last chance to persist. Nothing below may throw: an exception on the way out would
        // replace the real reason for closing with a confusing one.
        _exiting = true;
        try
        {
            SaveSettings();
        }
        catch (Exception exception)
        {
            LogError(exception);
        }

        DisposeQuietly(_hotKeys);
        DisposeQuietly(_tray);
        DisposeQuietly(_capture);
        DisposeQuietly(_scriptSave);

        base.OnExit(e);
    }

    /// <summary>
    /// Nothing has a window handle before this point, so the capture guard and the hotkeys are
    /// bound to the prompter here. Both window handlers go through the same path, which is what
    /// keeps the registrations from happening twice.
    /// </summary>
    private void OnPrompterSourceInitialized(object? sender, EventArgs e)
    {
        AttachToWindow();
        ApplyCaptureExclusion();
        RefreshTray();
    }

    /// <summary>
    /// WPF rewrites the native window state while the first frame is composed, which silently
    /// drops the capture affinity, so it is applied again once the content is on screen.
    /// </summary>
    private void OnPrompterContentRendered(object? sender, EventArgs e)
    {
        if (!_windowAttached)
        {
            return;
        }

        ApplyCaptureExclusion();
        RefreshTray();
    }

    /// <summary>
    /// Binds the services that need a live HWND to the prompter and returns the hotkey service.
    /// Called from <see cref="OnPrompterSourceInitialized"/>, and again from the editor request
    /// should it ever arrive before the window exists.
    /// </summary>
    private GlobalHotKeyService? AttachToWindow()
    {
        if (_prompter is null)
        {
            return null;
        }

        var hwnd = new WindowInteropHelper(_prompter).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        _capture?.AttachWindow(hwnd);
        _windowAttached = true;

        if (_hotKeys is null)
        {
            var hotKeys = new GlobalHotKeyService(hwnd);
            hotKeys.CommandInvoked += OnCommandInvoked;
            hotKeys.RegisterDefaults();
            _hotKeys = hotKeys;

            RebindPersistedGestures();
        }

        return _hotKeys;
    }

    /// <summary>
    /// Restores the gestures the user rebound last time. A combination another program owns can
    /// still be refused, so every command is attempted and the refused ones are reported instead
    /// of aborting the rest.
    /// </summary>
    private void RebindPersistedGestures()
    {
        if (_hotKeys is null || _settings?.HotKeys is not { } persisted)
        {
            return;
        }

        var refused = 0;
        foreach (var (name, gesture) in persisted)
        {
            if (!TryParseCommand(name, out var command))
            {
                continue;
            }

            // A gesture that is already the registered one cannot become free by being offered
            // again, so it is left alone and never counted as refused.
            if (string.Equals(gesture, _hotKeys.GestureFor(command), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (!_hotKeys.TryRebind(command, gesture))
                {
                    refused++;
                }
            }
            catch (ArgumentException)
            {
                refused++;
            }
        }

        if (refused > 0)
        {
            _tray?.Notify(
                "Горячие клавиши",
                $"{refused} сочетаний из прошлого запуска заняты другой программой — их можно переназначить в окне сценария.");
        }
    }

    private static bool TryParseCommand(string name, out AppCommand command)
    {
        foreach (var candidate in AppCommands.All)
        {
            if (string.Equals(candidate.ToString(), name, StringComparison.Ordinal))
            {
                command = candidate;
                return true;
            }
        }

        command = default;
        return false;
    }

    private void OnCommandInvoked(object? sender, AppCommand command) => Execute(command);

    private void OnCommandRequested(object? sender, AppCommand command) => Execute(command);

    /// <summary>
    /// The single place a command takes effect, so a hotkey and a tray click cannot drift apart.
    /// </summary>
    private void Execute(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.ToggleCaptureExclusion:
                ToggleCaptureExclusion();
                break;
            case AppCommand.ToggleClickThrough:
                ToggleClickThrough();
                break;
            case AppCommand.ToggleScrolling:
                ToggleScrolling();
                break;
            case AppCommand.ResetScroll:
                _prompter?.ResetScroll();
                break;
            case AppCommand.ToggleEditor:
                ShowEditor();
                break;
            case AppCommand.HideOrQuit:
                HideOrQuit();
                break;
        }
    }

    private void ToggleCaptureExclusion()
    {
        if (_settings is null)
        {
            return;
        }

        _settings.CaptureExcluded = !_settings.CaptureExcluded;
        ApplyCaptureExclusion();
        RefreshTray();
        SaveSettings();
    }

    private void ToggleClickThrough()
    {
        if (_settings is null)
        {
            return;
        }

        _settings.ClickThrough = !_settings.ClickThrough;
        _prompter?.SetClickThrough(_settings.ClickThrough);
        RefreshTray();
        SaveSettings();
    }

    private void ToggleScrolling()
    {
        if (_prompter is null)
        {
            return;
        }

        _prompter.SetScrollingRunning(!_prompter.IsScrollingRunning);
        RefreshTray();
    }

    private void OnEditorRequested(object? sender, EventArgs e) => ShowEditor();

    /// <summary>
    /// Creates the editor on the first request and shows it afterwards. Nothing about the prompter
    /// depends on it, so it stays closed for as long as the user does not ask for it.
    /// </summary>
    private void ShowEditor()
    {
        if (_editor is null)
        {
            var hotKeys = AttachToWindow();
            if (_settings is null || _scriptStore is null || hotKeys is null)
            {
                return;
            }

            var editor = new EditorWindow(_settings, _scriptText, _scriptStore, hotKeys);
            editor.SetCaptureStatus(_captureStatus);
            editor.ScriptTextEdited += OnEditorScriptTextEdited;
            editor.SettingsEdited += OnEditorSettingsEdited;
            editor.Closed += OnEditorClosed;
            _editor = editor;
        }

        if (!_editor.IsVisible)
        {
            _editor.Show();
        }

        _editor.RefreshScriptList();
        _editor.Activate();
        RefreshTray();
    }

    /// <summary>
    /// Puts the prompter back on screen. <see cref="HideOrQuit"/> is the only way to hide it and
    /// the tray is the only thing left on screen afterwards, so this is what keeps the hidden
    /// state recoverable without a restart.
    /// </summary>
    private void OnPrompterRequested(object? sender, EventArgs e)
    {
        if (_prompter is null)
        {
            return;
        }

        if (!_prompter.IsVisible)
        {
            _prompter.Show();
            _prompter.Activate();
        }

        RefreshTray();
    }

    /// <summary>
    /// The prompter has a system menu but no caption, so Alt+F4 reaches it and closes it for good:
    /// a closed WPF window cannot be shown again. Treating the request as a hide keeps the tray
    /// item above working, and the exit still happens through «Выход».
    /// </summary>
    private void OnPrompterClosing(object? sender, CancelEventArgs e)
    {
        // The shutdown itself closes this window: cancelling it there would block the exit.
        if (_exiting)
        {
            return;
        }

        e.Cancel = true;
        _prompter?.Hide();
        RefreshTray();
    }

    /// <summary>
    /// Hides the editor when it is open and the prompter otherwise. Leaving the application has
    /// its own tray item: a keystroke should not end it unnoticed.
    /// </summary>
    private void HideOrQuit()
    {
        if (_editor is { IsVisible: true })
        {
            _editor.Hide();
            return;
        }

        _prompter?.Hide();
    }

    /// <summary>
    /// Pushes the current mode to the system and rebuilds the status from what the guard read
    /// back: the return value of the call says nothing about the pixels that end up in a capture.
    /// </summary>
    private void ApplyCaptureExclusion()
    {
        if (!_windowAttached || _capture is null || _settings is null)
        {
            return;
        }

        var request = _settings.CaptureExcluded
            ? CaptureExclusionRequest.ExcludeFromCapture
            : CaptureExclusionRequest.None;

        _captureStatus = DescribeCapture(_capture.Apply(request), _capture.DiagnosticMessage);
        _editor?.SetCaptureStatus(_captureStatus);
    }

    private static string DescribeCapture(CaptureExclusionState state, string diagnostic) => state switch
    {
        CaptureExclusionState.ExcludedFromCapture => "Невидимый режим: включён",
        CaptureExclusionState.BlackBoxOnly =>
            "Невидимый режим: чёрный прямоугольник в кадре (система подменила WDA_MONITOR)",
        CaptureExclusionState.Visible => "Невидимый режим: выключен, окно видно в записи экрана",
        _ => $"Невидимый режим: не работает — {diagnostic}",
    };

    private void OnPrompterUserSettingChanged(object? sender, EventArgs e) => SaveSettings();

    private void OnEditorScriptTextEdited(object? sender, EventArgs e)
    {
        if (CurrentEditor(sender) is null)
        {
            return;
        }

        _scriptSave?.Request(PushScriptFromEditor);
    }

    /// <summary>
    /// A closed editor is flushed and then forgotten. The flush has to come first: the pending
    /// save reads the text back through <see cref="_editor"/>, which is released below. After
    /// that the window is unsubscribed and the reference dropped, because a closed window cannot
    /// be shown again and a late event from it must not reach the prompter or the disk.
    /// </summary>
    private void OnEditorClosed(object? sender, EventArgs e)
    {
        _scriptSave?.Flush();

        if (CurrentEditor(sender) is not { } editor)
        {
            return;
        }

        editor.ScriptTextEdited -= OnEditorScriptTextEdited;
        editor.SettingsEdited -= OnEditorSettingsEdited;
        editor.Closed -= OnEditorClosed;
        _editor = null;
    }

    /// <summary>
    /// Only the editor this class currently owns may report what happened in it. A window that
    /// has been closed is no longer that one, which is what keeps its late events inert.
    /// </summary>
    private EditorWindow? CurrentEditor(object? sender)
        => sender is EditorWindow editor && ReferenceEquals(editor, _editor) ? editor : null;

    private void PushScriptFromEditor()
    {
        if (_editor is null)
        {
            return;
        }

        _scriptText = ScriptText.Normalize(_editor.ScriptText);
        _scriptStore?.SaveCurrentText(_scriptText);
        _prompter?.SetScriptText(_scriptText);
    }

    private void OnEditorSettingsEdited(object? sender, EventArgs e)
    {
        if (CurrentEditor(sender) is not { } editor)
        {
            return;
        }

        var edited = AdoptLiveValues(editor.Settings);
        _settings = edited;
        _prompter?.ApplySettings(edited);
        SaveSettings();
    }

    /// <summary>
    /// The editor works on a clone taken when it opened, so the values this class owns while the
    /// application runs — the window geometry and the two live modes — are carried over instead of
    /// being replaced by that older snapshot.
    /// </summary>
    private SuflerSettings AdoptLiveValues(SuflerSettings edited)
    {
        edited.Normalize();

        if (_settings is { } live)
        {
            edited.WindowLeft = live.WindowLeft;
            edited.WindowTop = live.WindowTop;
            edited.WindowWidth = live.WindowWidth;
            edited.WindowHeight = live.WindowHeight;
            edited.CaptureExcluded = live.CaptureExcluded;
            edited.ClickThrough = live.ClickThrough;
        }

        return edited;
    }

    /// <summary>
    /// Writes the live settings, remembering the gestures that are registered right now: the
    /// editor owns the rebind UI, so this is where a rebind becomes persistent.
    /// </summary>
    private void SaveSettings()
    {
        if (_settings is null || _settingsStore is null)
        {
            return;
        }

        if (_hotKeys is not null)
        {
            _settings.HotKeys = AppCommands.All.ToDictionary(
                command => command.ToString(),
                command => _hotKeys.GestureFor(command));
        }

        _settingsStore.Save(_settings);
    }

    private void RefreshTray()
    {
        if (_tray is null)
        {
            return;
        }

        _tray.ToolTip = ToolTipText;
        _tray.Refresh();
    }

    private AppTrayState BuildTrayState() => new()
    {
        CaptureExcluded = _settings?.CaptureExcluded == true,
        CaptureState = _capture?.State ?? CaptureExclusionState.Visible,
        ClickThrough = _prompter?.ClickThrough == true,
        IsRunning = _prompter?.IsScrollingRunning == true,
        CaptureStatus = _captureStatus,
        Scripts = _scriptStore?.ListSavedScripts() ?? Array.Empty<string>(),
        // The working script always lives in current.txt, so a path here is the file the user
        // picked from the tray submenu.
        CurrentScriptPath = _currentScriptPath,
    };

    private void OnScriptRequested(object? sender, string path)
    {
        if (_scriptStore is null)
        {
            return;
        }

        _scriptText = ScriptText.Normalize(_scriptStore.OpenFile(path));
        _currentScriptPath = path;
        _scriptStore.SaveCurrentText(_scriptText);
        _prompter?.SetScriptText(_scriptText);
        _editor?.SetScriptText(_scriptText);
        RefreshTray();
    }

    // _exiting is set before Shutdown() because that is what closes the windows and therefore what
    // raises Closing: OnExit runs too late to let the prompter close itself.
    private void OnExitRequested(object? sender, EventArgs e)
    {
        _exiting = true;
        Shutdown();
    }

    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        // Windows closes the windows after this event, so the prompter must stop vetoing closes.
        _exiting = true;
        SaveSettings();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        MessageBox.Show(
            $"Непредвиденная ошибка: {e.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"Подробности записаны в {ErrorLogPath}",
            WindowName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Left unhandled on purpose: an application running on with an unknown error is worse than
        // a crash the user sees and can report.
        e.Handled = false;
    }

    private static string ErrorLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Sufler",
        "error.log");

    /// <summary>Appends the failure to error.log. Writing the log must never throw itself.</summary>
    private static void LogError(Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(ErrorLogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = string.Concat(
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Environment.NewLine,
                exception.ToString(),
                Environment.NewLine,
                new string('-', 60),
                Environment.NewLine);
            File.AppendAllText(ErrorLogPath, entry, Encoding.UTF8);
        }
        catch (Exception)
        {
        }
    }

    private static void DisposeQuietly(IDisposable? service)
    {
        try
        {
            service?.Dispose();
        }
        catch (Exception exception)
        {
            LogError(exception);
        }
    }
}
