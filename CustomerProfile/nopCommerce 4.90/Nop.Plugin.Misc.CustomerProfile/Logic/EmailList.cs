using Nop.Core;

namespace Nop.Plugin.Misc.CustomerProfile.Logic;

public static class EmailList
{
    /// <summary>Splits a comma- or semicolon-separated list into trimmed, non-empty entries</summary>
    public static IReadOnlyList<string> Split(string? value) =>
        (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Valid addresses joined with ';', the only separator nopCommerce's send task understands for CC</summary>
    public static string ToCcField(string? value) =>
        string.Join(";", Split(value).Where(CommonHelper.IsValidEmail).Distinct(StringComparer.OrdinalIgnoreCase));
}
