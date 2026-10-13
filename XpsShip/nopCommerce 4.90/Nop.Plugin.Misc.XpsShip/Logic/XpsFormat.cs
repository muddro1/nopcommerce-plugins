using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nop.Plugin.Misc.XpsShip.Logic;

/// <summary>XPS wants decimals as strings: 2 places for money, 4 for weight</summary>
public static class XpsFormat
{
    public static string Money(decimal value) => Math.Round(value, 2).ToString("0.00", CultureInfo.InvariantCulture);

    public static string Weight(decimal value) => Math.Round(value, 4).ToString("0.0000", CultureInfo.InvariantCulture);

    public static long UnixSeconds(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    /// <summary>Digits only, keeping the last 10 (drops a leading US country code)</summary>
    public static string? Phone(string? phone)
    {
        if (string.IsNullOrEmpty(phone))
            return null;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return null;

        return digits.Length > 10 ? digits.Substring(digits.Length - 10) : digits;
    }

    /// <summary>An invariant-culture decimal from XPS's form fields, or null</summary>
    public static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;

    /// <summary>Constant-time comparison, so response timing doesn't leak how much of a guessed key was right</summary>
    public static bool KeysMatch(string? supplied, string? expected)
    {
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expected))
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
    }

    private const string KeyAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string NewSecretKey(int length = XpsShipDefaults.SecretKeyLength)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = KeyAlphabet[RandomNumberGenerator.GetInt32(KeyAlphabet.Length)];

        return new string(chars);
    }

    /// <summary>Comma or newline separated setting values, trimmed, blanks dropped</summary>
    public static IReadOnlyList<string> SplitList(string? value) =>
        (value ?? string.Empty).Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .ToList();
}
