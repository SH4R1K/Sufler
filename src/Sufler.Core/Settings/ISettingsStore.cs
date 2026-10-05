namespace Sufler.Core.Settings;

/// <summary>
/// Persists <see cref="SuflerSettings"/> between application runs.
/// </summary>
public interface ISettingsStore
{
    string SettingsPath { get; }

    SuflerSettings Load();

    void Save(SuflerSettings settings);
}