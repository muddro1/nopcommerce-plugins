using System.Globalization;
using System.Text;

namespace Nop.Plugin.Misc.CustomerProfile.Logic;

/// <summary>CSV lines for the data export, safe to open in Excel</summary>
public static class Csv
{
    private static readonly char[] _needsQuotes = { ',', '"', '\n', '\r' };
    private static readonly char[] _formulaStarts = { '=', '+', '-', '@', '\t' };

    public static string Line(params object?[] values) => string.Join(",", values.Select(Field));

    private static string Field(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool flag => flag ? "Yes" : "No",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        // Text starting with these runs as a formula in Excel (CSV injection); a leading quote shows it as text
        if (value is string && text.Length > 0 && _formulaStarts.Contains(text[0]))
            text = "'" + text;

        return text.IndexOfAny(_needsQuotes) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    /// <summary>A file name with no directory parts or characters that are invalid in a zip entry on Windows</summary>
    public static string SafeFileName(string? name)
    {
        var lastPart = (name ?? string.Empty).Replace('\\', '/').Split('/').Last();
        var builder = new StringBuilder();
        foreach (var c in lastPart.Trim())
            builder.Append(Path.GetInvalidFileNameChars().Contains(c) || ":*?\"<>|".Contains(c) ? '_' : c);

        var safe = builder.ToString().Trim('.', ' ');
        return safe.Length == 0 ? "file" : safe;
    }

    /// <summary>UTF-8 with BOM, so Excel opens accented names correctly</summary>
    public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
}
