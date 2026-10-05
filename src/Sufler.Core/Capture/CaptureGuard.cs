namespace Sufler.Core.Capture;

public enum CaptureExclusionRequest
{
    None,
    ExcludeFromCapture,
    MonitorOnly,
}

public enum CaptureExclusionState
{
    Visible,
    ExcludedFromCapture,
    BlackBoxOnly,
    NotWorking,
}

/// <summary>
/// Platform-specific exclusion of the prompt window from screen capture.
/// </summary>
public interface ICaptureGuard
{
    CaptureExclusionState State { get; }

    string DiagnosticMessage { get; }

    CaptureExclusionState Apply(CaptureExclusionRequest request);

    void Reset();
}