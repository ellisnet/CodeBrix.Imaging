using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats;

/// <summary>
/// A placeholder <see cref="IImageFormat"/> used when the format of an image is not known.
/// <para>
/// An image carries this format when it was created in memory rather than decoded from
/// encoded bytes - for example via <c>new Image&lt;Rgba32&gt;(width, height)</c> - and has not
/// been saved yet. It is the initial value of
/// <see cref="Metadata.ImageMetadata.ExpectedFormat"/>.
/// </para>
/// <para>
/// This format has no encoder or decoder: asking
/// <see cref="ImageFormatManager.FindEncoder"/> or
/// <see cref="ImageFormatManager.FindDecoder"/> for one throws
/// <see cref="System.NotSupportedException"/>. Specify the desired format explicitly when
/// saving such an image, for example <c>image.Save(stream, PngFormat.Instance)</c>.
/// </para>
/// </summary>
public sealed class UnknownImageFormat : IImageFormat
{
    private const string FormatName = "unknown";

    /// <summary>
    /// The MIME type reported for an image whose format is not known.
    /// </summary>
    public const string FormatMimeType = "image/" + FormatName;

    /// <inheritdoc />
    public string Name => FormatName;

    /// <inheritdoc />
    public string DefaultMimeType => FormatMimeType;

    /// <inheritdoc />
    public IEnumerable<string> MimeTypes => [FormatMimeType];

    /// <inheritdoc />
    public IEnumerable<string> FileExtensions => [DefaultFileExtension];

    /// <inheritdoc />
    public string DefaultFileExtension => $".{FormatName}";

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static UnknownImageFormat Instance { get; } = new();
}
