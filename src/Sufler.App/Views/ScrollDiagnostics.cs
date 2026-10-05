using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Sufler.App.Views;

/// <summary>
/// One reading of the prompter as the scroll path saw it. Every field is a plain number so that a
/// value the layout has not produced yet reads as <c>NaN</c> or <c>0</c> instead of being absent:
/// "which of the three numbers was missing" is the whole question the file has to answer.
/// </summary>
internal readonly record struct ScrollDiagnosticsSnapshot
{
    /// <summary>Timer ticks since the prompter was loaded.</summary>
    public double Ticks { get; init; }

    public bool Running { get; init; }
    public bool Paused { get; init; }

    /// <summary>Offset the engine currently reports.</summary>
    public double Offset { get; init; }

    /// <summary>Offset at which the engine considers the script finished.</summary>
    public double Max { get; init; }

    public double LineHeight { get; init; }
    public double Content { get; init; }
    public double ViewportEngine { get; init; }

    public double Scrollable { get; init; }
    public double ViewportReal { get; init; }
    public double Extent { get; init; }

    /// <summary>Offset the <see cref="System.Windows.Controls.ScrollViewer"/> actually shows.</summary>
    public double VerticalOffset { get; init; }

    /// <summary>Value handed to <c>ScrollToVerticalOffset</c>.</summary>
    public double Target { get; init; }

    /// <summary>Upper bound <c>Target</c> was clamped to.</summary>
    public double ClampMax { get; init; }

    public int TextLength { get; init; }
    public bool HintVisible { get; init; }

    /// <summary>Time since the previous tick, in milliseconds.</summary>
    public double DeltaMs { get; init; }

    public bool ClickThrough { get; init; }

    /// <summary>Line height the prompter measured, before the engine ever saw it.</summary>
    public double RawLineHeight { get; init; }

    public double TextActualHeight { get; init; }
    public double ScrollerActualHeight { get; init; }

    /// <summary>Wheel delta of a wheel record, modifier mask of a key record.</summary>
    public double InputDelta { get; init; }
    public double Modifiers { get; init; }

    /// <summary>Which measured value made <c>PushMetrics</c> refuse to feed the engine.</summary>
    public string SkipCause { get; init; } = "-";

    public ScrollDiagnosticsSnapshot() => SkipCause = "-";
}

/// <summary>
/// Temporary tracing for the "the prompter reports that it scrolls and the text does not move"
/// report. The numbers that settle the question only exist inside a running process, so they go
/// to a file next to the rest of the application data. Every failure is swallowed on purpose: a
/// diagnostic that throws would replace the symptom it is looking for with a crash.
/// </summary>
internal static class ScrollDiagnostics
{
    private const string FileName = "scroll-diagnostics.log";

    private static bool _started;

    /// <summary>
    /// The same directory <c>JsonSettingsStore</c> resolves, so the file lands next to
    /// settings.json instead of in a place the user has to be told about twice.
    /// </summary>
    internal static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Sufler",
        FileName);

    /// <summary>
    /// Appends one record. The file is emptied once per process and then only ever appended to, one
    /// open and close per line, so a prompter killed mid-run still leaves everything it wrote.
    /// </summary>
    internal static void Log(string reason, ScrollDiagnosticsSnapshot snapshot)
    {
        try
        {
            var path = LogPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!_started)
            {
                File.WriteAllText(path, string.Empty, Encoding.UTF8);
                _started = true;
                File.AppendAllText(path, string.Concat(StartupLine(), Environment.NewLine), Encoding.UTF8);
            }

            File.AppendAllText(
                path,
                string.Concat(FormatRecord(reason, snapshot), Environment.NewLine),
                Encoding.UTF8);
        }
        catch (Exception)
        {
        }
    }

    private static string FormatRecord(string reason, ScrollDiagnosticsSnapshot snapshot) =>
        string.Join(
            ' ',
            DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Field("reason", reason),
            Field("ticks", snapshot.Ticks),
            Field("running", snapshot.Running),
            Field("paused", snapshot.Paused),
            Field("offset", snapshot.Offset),
            Field("max", snapshot.Max),
            Field("lineH", snapshot.LineHeight),
            Field("content", snapshot.Content),
            Field("viewportEngine", snapshot.ViewportEngine),
            Field("scrollable", snapshot.Scrollable),
            Field("vpReal", snapshot.ViewportReal),
            Field("extent", snapshot.Extent),
            Field("vOffset", snapshot.VerticalOffset),
            Field("target", snapshot.Target),
            Field("clampMax", snapshot.ClampMax),
            Field("textLen", snapshot.TextLength),
            Field("hintVisible", snapshot.HintVisible),
            Field("dtMs", snapshot.DeltaMs),
            Field("clickThrough", snapshot.ClickThrough),
            Field("rawLineH", snapshot.RawLineHeight),
            Field("textActualH", snapshot.TextActualHeight),
            Field("scrollerActualH", snapshot.ScrollerActualHeight),
            Field("inputDelta", snapshot.InputDelta),
            Field("mods", snapshot.Modifiers),
            Field("skipCause", snapshot.SkipCause));

    /// <summary>Marks the slice: a reader has to know which run produced the lines below.</summary>
    private static string StartupLine()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly?.GetName().Version?.ToString()
            ?? "unknown";

        return string.Join(
            ' ',
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Field("reason", "start"),
            Field("version", version),
            Field("runtime", Environment.Version.ToString()),
            Field("pid", Environment.ProcessId),
            Field("path", LogPath));
    }

    // "0.###" keeps a line short and, more to the point, prints NaN as NaN instead of as nothing at
    // all: an unreadable number has to be readable.
    private static string Field(string name, double value)
        => string.Concat(name, "=", value.ToString("0.###", CultureInfo.InvariantCulture));

    private static string Field(string name, int value)
        => string.Concat(name, "=", value.ToString(CultureInfo.InvariantCulture));

    private static string Field(string name, bool value)
        => value ? string.Concat(name, "=1") : string.Concat(name, "=0");

    private static string Field(string name, string value) => string.Concat(name, "=", value);
}