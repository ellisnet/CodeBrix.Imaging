// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Jpeg; //Was previously: namespace SixLabors.ImageSharp.Formats.Jpeg;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the jpeg format.
/// </summary>
public sealed class JpegFormat : IImageFormat<JpegMetadata>
{
    /// <summary>
    /// The display name of the JPEG format, as reported by <see cref="Name"/>.
    /// </summary>
    public const string FormatName = "JPEG";

    /// <summary>
    /// The alternate display name commonly used for the JPEG format.
    /// </summary>
    public const string FormatAltName = "JPG";

    /// <summary>
    /// The default MIME type of the JPEG format, as reported by <see cref="DefaultMimeType"/>.
    /// </summary>
    public const string FormatMimeType = "image/jpeg";

    /// <summary>
    /// The default file extension of the JPEG format, as reported by <see cref="DefaultFileExtension"/>.
    /// </summary>
    public const string FormatDefaultExtension = ".jpg";

    /// <summary>
    /// The alternate file extension recognised for the JPEG format.
    /// </summary>
    public const string FormatAltDefaultExtension = ".jpeg";

    private JpegFormat() { }

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static JpegFormat Instance { get; } = new JpegFormat();

    /// <inheritdoc/>
    public string Name => FormatName;

    /// <inheritdoc/>
    public string DefaultMimeType => FormatMimeType;

    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => JpegConstants.MimeTypes;

    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => JpegConstants.FileExtensions;

    /// <inheritdoc/>
    public string DefaultFileExtension => FormatDefaultExtension;

    /// <inheritdoc/>
    public JpegMetadata CreateDefaultFormatMetadata() => new JpegMetadata();
}