using System.IO;
using System.Text;
using Sufler.Core.Script;

namespace Sufler.App.Services;

/// <summary>
/// Stores the working script as <c>current.txt</c> and the named scripts as <c>*.txt</c> and
/// <c>*.md</c> inside <c>scripts\</c>, all under the application data directory. Writes go through
/// a temporary file so a crash in the middle of a save cannot truncate the previous content.
/// </summary>
public sealed class FileScriptStore : IScriptStore
{
    private const string ScriptsFolderName = "scripts";
    private const string CurrentFileName = "current.txt";
    private const string TxtExtension = ".txt";
    private const string MarkdownExtension = ".md";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;

    public FileScriptStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sufler"))
    {
    }

    public FileScriptStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string ScriptsDirectory => Path.Combine(_directory, ScriptsFolderName);

    public string CurrentTextPath => Path.Combine(_directory, CurrentFileName);

    public string LoadCurrentText()
        => File.Exists(CurrentTextPath) ? File.ReadAllText(CurrentTextPath) : string.Empty;

    public void SaveCurrentText(string text) => WriteAtomic(CurrentTextPath, ScriptText.Normalize(text));

    public IReadOnlyList<string> ListSavedScripts()
    {
        var directory = ScriptsDirectory;
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        // The folder is enumerated whole and the extension is compared here: a wildcard pattern
        // cannot tell a script from the "*.tmp" file a failed atomic write leaves behind, and an
        // exact comparison against the two accepted extensions rejects that file the same way it
        // rejects anything else the user did not save as a script.
        return Directory.EnumerateFiles(directory)
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                return string.Equals(extension, TxtExtension, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, MarkdownExtension, StringComparison.OrdinalIgnoreCase);
            })
            .Select(Path.GetFullPath)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string OpenFile(string path) => File.ReadAllText(Validated(path));

    public void SaveFile(string path, string text) => WriteAtomic(Validated(path), ScriptText.Normalize(text));

    private static string Validated(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var extension = Path.GetExtension(path);
        if (!string.Equals(extension, TxtExtension, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, MarkdownExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Недопустимое расширение «{extension}»: поддерживаются только .txt и .md.", nameof(path));
        }

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Writes through a temporary file so the destination is never half written.
    /// <c>File.Delete</c> is deliberately absent from this class: nothing the user saved is ever
    /// removed by it. The temporary file this method creates itself is the one exception — it is
    /// the store's own litter, it sits in the user's folder, and a refused «Сохранить как…» must
    /// not leave it behind. Only that path is ever deleted: never the destination and never a file
    /// the user named.
    /// </summary>
    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, content, Utf8WithoutBom);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(temp);
            throw;
        }
    }

    /// <summary>
    /// Removes the temporary file of a failed write. It never replaces the failure being reported:
    /// a temp file the file system refuses to delete is left alone, and the original exception is
    /// what the caller has to show.
    /// </summary>
    private static void TryDeleteTemp(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception)
        {
        }
    }
}