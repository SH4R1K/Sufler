using System.IO;
using System.Text.Json;
using Sufler.Core.Settings;

namespace Sufler.App.Services;

/// <summary>
/// Keeps the user configuration in <c>settings.json</c> under the application data directory.
/// A settings file that cannot be read never stops the application: defaults are returned and
/// the unreadable file is moved aside for later inspection.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private const string FileName = "settings.json";
    private const string CorruptFileName = "settings.corrupt.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _directory;

    public JsonSettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sufler"))
    {
    }

    public JsonSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string SettingsPath => Path.Combine(_directory, FileName);

    public SuflerSettings Load()
    {
        var path = SettingsPath;
        if (!File.Exists(path))
        {
            return Defaults();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<SuflerSettings>(File.ReadAllText(path), SerializerOptions);
            return settings is null ? Defaults() : Normalized(settings);
        }
        catch (JsonException)
        {
            SetAside(path);
            return Defaults();
        }
        catch (IOException)
        {
            return Defaults();
        }
    }

    public void Save(SuflerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();
        var snapshot = settings.Clone();
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);

        Directory.CreateDirectory(_directory);
        var path = SettingsPath;
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private static SuflerSettings Defaults() => Normalized(new SuflerSettings());

    private static SuflerSettings Normalized(SuflerSettings settings)
    {
        settings.Normalize();
        return settings;
    }

    private void SetAside(string path)
    {
        try
        {
            File.Move(path, Path.Combine(_directory, CorruptFileName), overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}