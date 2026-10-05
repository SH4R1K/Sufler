using System.Globalization;
using System.Windows.Interop;
using Sufler.App.Interop;
using Sufler.Core.HotKeys;

namespace Sufler.App.Services;

/// <summary>
/// Registers the global hotkeys of <see cref="AppCommand"/> on the prompt window and reports
/// which of them the system accepted. The window handle must belong to a window of the calling
/// thread, so every member is meant to be used on the UI thread and nothing takes a lock.
/// </summary>
public sealed class GlobalHotKeyService : IHotKeyService
{
    private const int WmHotKey = 0x0312;
    private const int HotKeyIdBase = 0x5150;

    private static readonly Dictionary<int, AppCommand> CommandByHotKeyId =
        AppCommands.All.ToDictionary(HotKeyId);

    private readonly IntPtr _window;
    private readonly HashSet<AppCommand> _registered = [];
    private readonly Dictionary<AppCommand, string> _gestures = [];
    private HwndSource? _source;

    public GlobalHotKeyService(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Не передан HWND окна: глобальные горячие клавиши некуда регистрировать.", nameof(windowHandle));
        }

        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException($"У окна {windowHandle} нет HwndSource: горячие клавиши можно регистрировать только после создания источника WPF.");
        _window = windowHandle;
        _source.AddHook(WndProc);
    }

    public IReadOnlyList<AppCommand> Registered => AppCommands.All.Where(_registered.Contains).ToArray();

    /// <summary>
    /// Commands whose gesture was offered to the system and refused, typically because
    /// another application owns the combination (ERROR_HOTKEY_ALREADY_REGISTERED, 1409).
    /// </summary>
    public IReadOnlyList<AppCommand> Unavailable => AppCommands.All
        .Where(command => _gestures.ContainsKey(command) && !_registered.Contains(command))
        .ToArray();

    public event EventHandler<AppCommand>? CommandInvoked;

    public string GestureFor(AppCommand command)
        => _gestures.TryGetValue(command, out var gesture) ? gesture : AppCommands.DefaultGesture(command);

    /// <summary>
    /// Offers the six default gestures to the system. A refused gesture only lands in
    /// <see cref="Unavailable"/> and does not stop the remaining registrations.
    /// </summary>
    public void RegisterDefaults()
    {
        foreach (var command in AppCommands.All)
        {
            var gesture = Gesture.Parse(AppCommands.DefaultGesture(command));
            ReleaseSlot(command);
            _gestures[command] = gesture.Text;
            if (RegisterSlot(command, gesture))
            {
                _registered.Add(command);
            }
        }
    }

    /// <summary>
    /// Replaces the gesture of <paramref name="command"/>. Throws <see cref="ArgumentException"/>
    /// with a Russian message when <paramref name="gesture"/> cannot be parsed. Returns false
    /// and gives the command its previous gesture back when the system refuses the new one.
    /// </summary>
    public bool TryRebind(AppCommand command, string gesture)
    {
        var parsed = Gesture.Parse(gesture);
        var previous = _gestures.GetValueOrDefault(command) ?? AppCommands.DefaultGesture(command);
        var hadSlot = ReleaseSlot(command);

        if (RegisterSlot(command, parsed))
        {
            _gestures[command] = parsed.Text;
            _registered.Add(command);
            return true;
        }

        if (hadSlot && RegisterSlot(command, Gesture.Parse(previous)))
        {
            _registered.Add(command);
        }
        else
        {
            _gestures[command] = parsed.Text;
        }

        return false;
    }

    public void Dispose()
    {
        var source = _source;
        if (source is not null)
        {
            _source = null;
            source.RemoveHook(WndProc);
        }

        foreach (var command in AppCommands.All)
        {
            ReleaseSlot(command);
        }
    }

    private static int HotKeyId(AppCommand command) => HotKeyIdBase + (int)command;

    /// <summary>
    /// Frees the registration slot of the command and reports whether the system held one.
    /// </summary>
    private bool ReleaseSlot(AppCommand command)
    {
        if (!_registered.Remove(command))
        {
            return false;
        }

        HotKeyNative.UnregisterHotKey(_window, HotKeyId(command));
        return true;
    }

    /// <summary>
    /// MOD_NOREPEAT is always added so that holding a key down fires the command once.
    /// </summary>
    private bool RegisterSlot(AppCommand command, Gesture gesture)
        => HotKeyNative.RegisterHotKey(
            _window,
            HotKeyId(command),
            gesture.Modifiers | HotKeyNative.ModNoRepeat,
            gesture.VirtualKey);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotKey)
        {
            return IntPtr.Zero;
        }

        handled = true;
        if (CommandByHotKeyId.TryGetValue((int)wParam.ToInt64(), out var command))
        {
            CommandInvoked?.Invoke(this, command);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// A parsed gesture: modifiers, virtual key and the canonical text handed back to the UI.
    /// </summary>
    private sealed record Gesture(uint Modifiers, uint VirtualKey, string Text)
    {
        private static readonly Dictionary<string, uint> ModifierBits = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = HotKeyNative.ModControl,
            ["Control"] = HotKeyNative.ModControl,
            ["Alt"] = HotKeyNative.ModAlt,
            ["Shift"] = HotKeyNative.ModShift,
            ["Win"] = HotKeyNative.ModWin,
            ["Windows"] = HotKeyNative.ModWin,
        };

        private static readonly (string Name, uint VirtualKey)[] NamedKeys =
        [
            ("Space", 0x20),
            ("Tab", 0x09),
            ("Enter", 0x0D),
            ("Return", 0x0D),
            ("Escape", 0x1B),
            ("Esc", 0x1B),
            ("Backspace", 0x08),
            ("Delete", 0x2E),
            ("Insert", 0x2D),
            ("Home", 0x24),
            ("End", 0x23),
            ("PageUp", 0x21),
            ("PageDown", 0x22),
            ("Left", 0x25),
            ("Up", 0x26),
            ("Right", 0x27),
            ("Down", 0x28),
            ("PrintScreen", 0x2C),
            ("Pause", 0x13),
            ("NumLock", 0x90),
            ("ScrollLock", 0x91),
        ];

        public static Gesture Parse(string? gesture)
        {
            if (string.IsNullOrWhiteSpace(gesture))
            {
                throw Invalid(gesture, "сочетание клавиш не указано");
            }

            var parts = gesture.Split('+', StringSplitOptions.TrimEntries);
            var keyPart = parts[^1];
            if (keyPart.Length == 0)
            {
                throw Invalid(gesture, "не указана клавиша");
            }

            var modifiers = 0u;
            foreach (var part in parts[..^1])
            {
                if (!ModifierBits.TryGetValue(part, out var bits))
                {
                    throw Invalid(gesture, $"модификатор «{part}» не распознан");
                }

                modifiers |= bits;
            }

            if (modifiers == 0)
            {
                throw Invalid(gesture, "нужен хотя бы один модификатор: Ctrl, Alt, Shift или Win");
            }

            if (!TryParseKey(keyPart, out var virtualKey, out var keyName))
            {
                throw Invalid(gesture, $"клавиша «{keyPart}» не поддерживается");
            }

            var names = new List<string>(5);
            if ((modifiers & HotKeyNative.ModControl) != 0)
            {
                names.Add("Ctrl");
            }

            if ((modifiers & HotKeyNative.ModAlt) != 0)
            {
                names.Add("Alt");
            }

            if ((modifiers & HotKeyNative.ModShift) != 0)
            {
                names.Add("Shift");
            }

            if ((modifiers & HotKeyNative.ModWin) != 0)
            {
                names.Add("Win");
            }

            names.Add(keyName);
            return new Gesture(modifiers, virtualKey, string.Join('+', names));
        }

        private static bool TryParseKey(string token, out uint virtualKey, out string name)
        {
            virtualKey = 0;
            name = string.Empty;

            if (token.Length == 1)
            {
                var symbol = char.ToUpperInvariant(token[0]);
                if (symbol is >= 'A' and <= 'Z' or >= '0' and <= '9')
                {
                    // For letters and digits the virtual key equals the character code.
                    virtualKey = (uint)(ushort)symbol;
                    name = symbol.ToString();
                    return true;
                }

                return false;
            }

            if (token[0] is 'F' or 'f'
                && int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var function)
                && function is >= 1 and <= 24)
            {
                // The F keys are contiguous: VK_F1 is 0x70.
                virtualKey = (uint)(0x6F + function);
                name = "F" + function.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            foreach (var known in NamedKeys)
            {
                if (string.Equals(known.Name, token, StringComparison.OrdinalIgnoreCase))
                {
                    virtualKey = known.VirtualKey;
                    name = known.Name;
                    return true;
                }
            }

            return false;
        }

        private static ArgumentException Invalid(string? gesture, string reason)
            => new($"Не удалось разобрать сочетание клавиш «{gesture}»: {reason}. Пример: Ctrl+Alt+H.", nameof(gesture));
    }
}