using Sufler.Core.Capture;

namespace Sufler.App.Services;

/// <summary>
/// Snapshot of everything the tray menu shows: check marks, the capture diagnostic and the
/// list of saved scripts.
/// </summary>
public sealed record AppTrayState
{
    public bool CaptureExcluded { get; init; }

    public CaptureExclusionState CaptureState { get; init; }

    public bool ClickThrough { get; init; }

    public bool IsRunning { get; init; }

    public string CaptureStatus { get; init; } = string.Empty;

    public IReadOnlyList<string> Scripts { get; init; } = Array.Empty<string>();

    public string? CurrentScriptPath { get; init; }
}