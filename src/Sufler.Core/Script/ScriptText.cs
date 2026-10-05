using System.Text;

namespace Sufler.Core.Script;

/// <summary>
/// Canonicalises raw text typed or pasted into the script editor so that every
/// consumer sees the same line structure.
/// </summary>
public static class ScriptText
{
    private const char Bom = '\uFEFF';
    private const char NewLine = '\n';

    /// <summary>
    /// Strips a leading BOM, converts CRLF and CR to LF, trims trailing
    /// whitespace of every line, drops leading and trailing empty lines and
    /// collapses runs of three or more newlines to exactly two (one blank line).
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var text = raw.TrimStart(Bom)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        var builder = new StringBuilder(text.Length);
        var pendingBlankLines = 0;
        var hasContent = false;

        foreach (var line in text.Split(NewLine))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
            {
                if (hasContent)
                {
                    pendingBlankLines++;
                }

                continue;
            }

            if (hasContent)
            {
                builder.Append(NewLine);
                if (pendingBlankLines > 0)
                {
                    builder.Append(NewLine);
                    pendingBlankLines = 0;
                }
            }

            builder.Append(trimmed);
            hasContent = true;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits normalized text into logical lines, keeping interior blank lines.
    /// Empty text yields an empty list rather than a single empty line.
    /// </summary>
    public static IReadOnlyList<string> SplitLogicalLines(string normalizedText)
        => string.IsNullOrEmpty(normalizedText)
            ? Array.Empty<string>()
            : normalizedText.Split(NewLine);
}