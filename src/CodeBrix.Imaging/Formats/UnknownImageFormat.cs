using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats;

public sealed class UnknownImageFormat : IImageFormat
{
    private const string FormatName = "unknown";
    public const string FormatMimeType = "image/" + FormatName;

    public string Name => FormatName;
    public string DefaultMimeType => FormatMimeType;
    public IEnumerable<string> MimeTypes => [FormatMimeType];
    public IEnumerable<string> FileExtensions => [$".{FormatName}"];

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static UnknownImageFormat Instance { get; } = new();
}
