// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Tiff.Constants;

namespace CodeBrix.Imaging.Formats.Tiff; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff;

/// <summary>
/// Encapsulates the means to encode and decode Tiff images.
/// </summary>
public sealed class TiffFormat : IImageFormat<TiffMetadata, TiffFrameMetadata>
{
    /// <summary>
    /// The display name of the TIFF format, as reported by <see cref="Name"/>.
    /// </summary>
    public const string FormatName = "TIFF";

    /// <summary>
    /// The default MIME type of the TIFF format, as reported by <see cref="DefaultMimeType"/>.
    /// </summary>
    public const string FormatMimeType = "image/tiff";

    /// <summary>
    /// The alternate MIME type recognised for the TIFF format.
    /// </summary>
    public const string FormatAltMimeType = "image/x-tiff";

    /// <summary>
    /// The default file extension of the TIFF format, as reported by <see cref="DefaultFileExtension"/>.
    /// </summary>
    public const string FormatDefaultExtension = ".tiff";

    /// <summary>
    /// The alternate file extension recognised for the TIFF format.
    /// </summary>
    public const string FormatAltDefaultExtension = ".tif";

    private TiffFormat() { }

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static TiffFormat Instance { get; } = new TiffFormat();

    /// <inheritdoc/>
    public string Name => FormatName;

    /// <inheritdoc/>
    public string DefaultMimeType => FormatMimeType;

    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => TiffConstants.MimeTypes;

    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => TiffConstants.FileExtensions;

    /// <inheritdoc/>
    public string DefaultFileExtension => FormatDefaultExtension;

    /// <inheritdoc/>
    public TiffMetadata CreateDefaultFormatMetadata() => new TiffMetadata();

    /// <inheritdoc/>
    public TiffFrameMetadata CreateDefaultFormatFrameMetadata() => new TiffFrameMetadata();
}