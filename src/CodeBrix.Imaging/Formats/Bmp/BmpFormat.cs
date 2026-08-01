// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Bmp; //Was previously: namespace SixLabors.ImageSharp.Formats.Bmp;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the bmp format.
/// </summary>
public sealed class BmpFormat : IImageFormat<BmpMetadata>
{
    /// <summary>
    /// The display name of the BMP format, as reported by <see cref="Name"/>.
    /// </summary>
    public const string FormatName = "BMP";

    /// <summary>
    /// The default MIME type of the BMP format, as reported by <see cref="DefaultMimeType"/>.
    /// </summary>
    public const string FormatMimeType = "image/bmp";

    /// <summary>
    /// The default file extension of the BMP format, as reported by <see cref="DefaultFileExtension"/>.
    /// </summary>
    public const string FormatDefaultExtension = ".bmp";

    private BmpFormat() { }

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static BmpFormat Instance { get; } = new BmpFormat();

    /// <inheritdoc/>
    public string Name => FormatName;

    /// <inheritdoc/>
    public string DefaultMimeType => FormatMimeType;

    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => BmpConstants.MimeTypes;

    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => BmpConstants.FileExtensions;

    /// <inheritdoc/>
    public string DefaultFileExtension => FormatDefaultExtension;

    /// <inheritdoc/>
    public BmpMetadata CreateDefaultFormatMetadata() => new BmpMetadata();


}