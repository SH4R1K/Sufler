using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sufler.Core.HotKeys;
using Sufler.Core.Script;
using Sufler.Core.Settings;

// System.Drawing arrives as an implicit using of this project, so the WPF types that share a
// simple name with it have to be pinned here.
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace Sufler.App;

/// <summary>
/// Script text plus every setting of the prompter. Emits events for the composition root,
/// which owns storage, the capture guard and the hotkey registrations; this window never
/// writes anything itself.
/// </summary>
public partial class EditorWindow : Window
{
    private const string WindowTitle = "Sufler — сценарий";

    private readonly IScriptStore _scriptStore;
    private readonly IHotKeyService _hotKeys;
    private readonly Dictionary<AppCommand, HotKeyRow> _hotKeyRows = new();
    private List<FontFamily> _fontFamilies = new();

    private SuflerSettings _settings;
    private bool _suppressEvents;
    private AppCommand? _capturingCommand;
    private (AppCommand Command, string Message)? _hotKeyMessage;

    public EditorWindow(SuflerSettings settings, string scriptText, IScriptStore scriptStore, IHotKeyService hotKeys)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(scriptStore);
        ArgumentNullException.ThrowIfNull(hotKeys);

        _scriptStore = scriptStore;
        _hotKeys = hotKeys;
        _settings = settings.Clone();
        _settings.Normalize();

        InitializeComponent();

        LoadFontFamilies();
        BuildHotKeyRows();

        PreviewKeyDown += OnEditorPreviewKeyDown;

        _suppressEvents = true;
        try
        {
            ScriptTextBox.Text = scriptText ?? string.Empty;
            PushSettingsToControls();
        }
        finally
        {
            _suppressEvents = false;
        }

        RefreshScriptList();
        RefreshHotKeyState();
    }

    /// <summary>The user typed or pasted in the script box.</summary>
    public event EventHandler? ScriptTextEdited;

    /// <summary>The user changed one of the settings controls.</summary>
    public event EventHandler? SettingsEdited;

    public string ScriptText => ScriptTextBox.Text;

    public SuflerSettings Settings
    {
        get
        {
            var clone = _settings.Clone();
            clone.Normalize();
            return clone;
        }
    }

    public void SetScriptText(string text)
    {
        _suppressEvents = true;
        try
        {
            ScriptTextBox.Text = text ?? string.Empty;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>
    /// Pushes new values into every control without reporting them back as user edits.
    /// </summary>
    public void ApplySettings(SuflerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings.Clone();
        normalized.Normalize();
        _settings = normalized;

        _suppressEvents = true;
        try
        {
            PushSettingsToControls();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    public void RefreshScriptList()
    {
        var entries = new List<ScriptListEntry>();
        foreach (var path in _scriptStore.ListSavedScripts())
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                entries.Add(new ScriptListEntry(path));
            }
        }

        var selectedPath = SelectedScriptPath();
        ScriptListBox.ItemsSource = entries;
        foreach (var entry in entries)
        {
            if (selectedPath is not null && SamePath(entry.FullPath, selectedPath))
            {
                ScriptListBox.SelectedItem = entry;
                break;
            }
        }

        ScriptsEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Re-reads the gestures and registration state from the hotkey service.
    /// </summary>
    public void RefreshHotKeyState()
    {
        foreach (var row in _hotKeyRows)
        {
            row.Value.SetGesture(_hotKeys.GestureFor(row.Key));
            row.Value.SetStatus(_hotKeys.Unavailable.Contains(row.Key)
                ? "занята другой программой"
                : "не зарегистрирована");
        }

        if (_hotKeyMessage is { } message && _hotKeyRows.TryGetValue(message.Command, out var failedRow))
        {
            failedRow.SetStatus(message.Message);
            _hotKeyMessage = null;
        }

        foreach (var row in _hotKeyRows)
        {
            row.Value.SetCapturing(_capturingCommand == row.Key);
        }
    }

    public void SetCaptureStatus(string text) => CaptureStatusText.Text = text ?? string.Empty;

    private void OnScriptTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        ScriptTextEdited?.Invoke(this, EventArgs.Empty);
    }

    private void OnFontFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || FontFamilyBox.SelectedItem is not FontFamily family)
        {
            return;
        }

        _settings.FontFamily = family.Source;
        SettingsEdited?.Invoke(this, EventArgs.Empty);
    }

    private void OnFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateReadouts();
        if (_suppressEvents)
        {
            return;
        }

        _settings.FontSize = e.NewValue;
        SettingsEdited?.Invoke(this, EventArgs.Empty);
    }

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateReadouts();
        if (_suppressEvents)
        {
            return;
        }

        _settings.LinesPerMinute = e.NewValue;
        SettingsEdited?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateReadouts();
        if (_suppressEvents)
        {
            return;
        }

        _settings.Opacity = e.NewValue;
        SettingsEdited?.Invoke(this, EventArgs.Empty);
    }

    private void OnLoopChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.Loop = LoopCheckBox.IsChecked == true;
        SettingsEdited?.Invoke(this, EventArgs.Empty);
    }

    private void LoadFontFamilies()
    {
        _fontFamilies = Fonts.SystemFontFamilies
            .OrderBy(family => family.Source, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Keep a configured family visible even when it is not installed, otherwise the combo
        // would silently fall back and the next edit would overwrite the setting.
        if (_fontFamilies.All(family => !string.Equals(family.Source, _settings.FontFamily, StringComparison.OrdinalIgnoreCase)))
        {
            _fontFamilies.Insert(0, new FontFamily(_settings.FontFamily));
        }

        FontFamilyBox.ItemsSource = _fontFamilies;
    }

    private void PushSettingsToControls()
    {
        FontFamilyBox.SelectedItem = _fontFamilies
            .FirstOrDefault(family => string.Equals(family.Source, _settings.FontFamily, StringComparison.OrdinalIgnoreCase));
        FontSizeSlider.Value = _settings.FontSize;
        SpeedSlider.Value = _settings.LinesPerMinute;
        OpacitySlider.Value = _settings.Opacity;
        LoopCheckBox.IsChecked = _settings.Loop;
        UpdateReadouts();
    }

    private void UpdateReadouts()
    {
        FontSizeValueText.Text = FontSizeSlider.Value.ToString("0", CultureInfo.CurrentUICulture);
        SpeedValueText.Text = string.Format(CultureInfo.CurrentUICulture, "{0:0} строк/мин", SpeedSlider.Value);
        OpacityValueText.Text = string.Format(CultureInfo.CurrentUICulture, "{0:0} %", OpacitySlider.Value * 100d);
    }

    private void OnLoadScriptClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Открыть сценарий",
            Filter = "Текстовые сценарии (*.txt;*.md)|*.txt;*.md|Все файлы (*.*)|*.*",
            InitialDirectory = ExistingScriptsDirectory(),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = dialog.FileName;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        SetScriptText(_scriptStore.OpenFile(path));

        // SetScriptText is silent on purpose, but a file the user just opened did become the
        // working script, so the composition root has to hear about it.
        ScriptTextEdited?.Invoke(this, EventArgs.Empty);
        UpdateTitle(path);
        RefreshScriptList();
        SelectScript(path);
    }

    private void OnSaveScriptAsClick(object sender, RoutedEventArgs e)
    {
        var current = SelectedScriptPath();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Сохранить сценарий",
            Filter = "Текстовый сценарий (*.txt)|*.txt|Markdown (*.md)|*.md",
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = ExistingScriptsDirectory(),
            FileName = string.IsNullOrWhiteSpace(current) ? "Сценарий.txt" : Path.GetFileName(current),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = dialog.FileName;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _scriptStore.SaveFile(path, ScriptText);
        UpdateTitle(path);
        RefreshScriptList();
        SelectScript(path);
    }

    private string? SelectedScriptPath() => (ScriptListBox.SelectedItem as ScriptListEntry)?.FullPath;

    private void SelectScript(string fullPath)
    {
        foreach (var entry in ScriptListBox.Items.OfType<ScriptListEntry>())
        {
            if (SamePath(entry.FullPath, fullPath))
            {
                ScriptListBox.SelectedItem = entry;
                return;
            }
        }
    }

    private static bool SamePath(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private string ExistingScriptsDirectory()
    {
        var directory = _scriptStore.ScriptsDirectory;
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) ? directory : string.Empty;
    }

    private void UpdateTitle(string? path)
        => Title = string.IsNullOrWhiteSpace(path) ? WindowTitle : $"{WindowTitle} — {Path.GetFileName(path)}";

    private void BuildHotKeyRows()
    {
        foreach (var command in AppCommands.All)
        {
            var row = new HotKeyRow(Describe(command), () => BeginCapture(command));
            _hotKeyRows.Add(command, row);
            HotKeyRowsPanel.Children.Add(row.Element);
        }
    }

    private static string Describe(AppCommand command) => command switch
    {
        AppCommand.ToggleCaptureExclusion => "Скрыть суфлёр от записи экрана",
        AppCommand.ToggleClickThrough => "Клики проходят насквозь",
        AppCommand.ToggleScrolling => "Пуск и пауза прокрутки",
        AppCommand.ResetScroll => "В начало",
        AppCommand.ToggleEditor => "Открыть или закрыть окно сценария",
        AppCommand.HideOrQuit => "Свернуть суфлёр или выход",
        _ => command.ToString(),
    };

    private void BeginCapture(AppCommand command)
    {
        _capturingCommand = command;
        _hotKeyMessage = null;
        RefreshHotKeyState();
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingCommand is not { } command)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _capturingCommand = null;
            _hotKeyMessage = (command, "изменение отменено");
            RefreshHotKeyState();
            return;
        }

        if (IsModifierKey(e.Key))
        {
            return;
        }

        var gesture = FormatGesture(e.Key, Keyboard.Modifiers);
        if (gesture is null)
        {
            _hotKeyMessage = (command, "нужна клавиша вместе с Ctrl, Alt или Shift");
            RefreshHotKeyState();
            return;
        }

        _capturingCommand = null;
        if (!_hotKeys.TryRebind(command, gesture))
        {
            _hotKeyMessage = (command, "не удалось зарегистрировать — сочетание занято другой программой");
        }

        RefreshHotKeyState();
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;

    /// <summary>
    /// Builds a gesture string in a stable modifier order, or null when the key would be
    /// captured on its own.
    /// </summary>
    private static string? FormatGesture(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) == 0)
        {
            return null;
        }

        var parts = new List<string>(5);
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(key.ToString());
        return string.Join('+', parts);
    }

    /// <summary>A saved script: the list shows the file name, the item carries the full path.</summary>
    private sealed class ScriptListEntry
    {
        public ScriptListEntry(string fullPath) => FullPath = fullPath;

        public string FullPath { get; }

        public string DisplayName => Path.GetFileName(FullPath);
    }

    /// <summary>One hotkey line: description, gesture, capture button and status.</summary>
    private sealed class HotKeyRow
    {
        private const string CaptureLabel = "изменить";
        private const string CaptureHint = "нажмите сочетание…";

        private readonly TextBlock _gesture;
        private readonly TextBlock _status;
        private readonly Button _capture;

        public HotKeyRow(string description, Action captureRequested)
        {
            var caption = new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _gesture = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0),
            };

            _capture = new Button
            {
                Content = CaptureLabel,
                Padding = new Thickness(8, 2, 8, 2),
                MinWidth = 110,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _capture.Click += (_, _) => captureRequested();

            _status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            Element = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            Element.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Element.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            Element.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Element.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

            Grid.SetColumn(caption, 0);
            Grid.SetColumn(_gesture, 1);
            Grid.SetColumn(_capture, 2);
            Grid.SetColumn(_status, 3);

            Element.Children.Add(caption);
            Element.Children.Add(_gesture);
            Element.Children.Add(_capture);
            Element.Children.Add(_status);
        }

        public Grid Element { get; }

        public void SetGesture(string gesture) => _gesture.Text = gesture;

        public void SetStatus(string status) => _status.Text = status;

        public void SetCapturing(bool capturing) => _capture.Content = capturing ? CaptureHint : CaptureLabel;
    }
}