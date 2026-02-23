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
    public const string FormatName = "TIFF";
    public const string FormatMimeType = "image/tiff";
    public const string FormatAltMimeType = "image/x-tiff";
    public const string FormatDefaultExtension = ".tiff";
    public const string FormatAltDefaultExtension = ".tif";

    private TiffFormat()
    {
    }

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
    public TiffMetadata CreateDefaultFormatMetadata() => new TiffMetadata();

    /// <inheritdoc/>
    public TiffFrameMetadata CreateDefaultFormatFrameMetadata() => new TiffFrameMetadata();
}