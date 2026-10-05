namespace Sufler.Core.Script;

/// <summary>
/// Stores the working script and the named scripts saved on disk.
/// </summary>
public interface IScriptStore
{
    string ScriptsDirectory { get; }

    string CurrentTextPath { get; }

    string LoadCurrentText();

    void SaveCurrentText(string text);

    IReadOnlyList<string> ListSavedScripts();

    string OpenFile(string path);

    void SaveFile(string path, string text);
}