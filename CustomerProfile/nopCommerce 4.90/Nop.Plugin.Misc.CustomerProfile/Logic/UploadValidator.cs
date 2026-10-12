namespace Nop.Plugin.Misc.CustomerProfile.Logic;

public enum UploadProblem
{
    None,
    Empty,
    TooLarge,
    UnsupportedType
}

public static class UploadValidator
{
    private static readonly (string Extension, string ContentType, byte[] Magic)[] _allowed =
    {
        (".pdf", "application/pdf", "%PDF"u8.ToArray()),
        (".png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        (".jpg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
        (".jpeg", "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF })
    };

    public const string AcceptAttribute = ".pdf,.png,.jpg,.jpeg";

    /// <summary>
    /// Checks a tax certificate upload. The extension must match the file's first bytes, so a renamed file is refused.
    /// </summary>
    /// <returns>The problem, and the content type to store (taken from the bytes, not from the browser)</returns>
    public static (UploadProblem Problem, string? ContentType) Validate(string? fileName, ReadOnlySpan<byte> bytes, int maxSizeMb)
    {
        if (bytes.Length == 0)
            return (UploadProblem.Empty, null);

        if (bytes.Length > maxSizeMb * 1024L * 1024L)
            return (UploadProblem.TooLarge, null);

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        foreach (var (allowedExtension, contentType, magic) in _allowed)
        {
            if (extension == allowedExtension && bytes.StartsWith(magic))
                return (UploadProblem.None, contentType);
        }

        return (UploadProblem.UnsupportedType, null);
    }
}
