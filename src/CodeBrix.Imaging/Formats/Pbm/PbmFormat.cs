// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Pbm; //Was previously: namespace SixLabors.ImageSharp.Formats.Pbm;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the PBM format.
/// </summary>
public sealed class PbmFormat : IImageFormat<PbmMetadata>
{
    public const string FormatName = "PBM";
    public const string FormatMimeType = "image/x-portable-pixmap";
    public const string FormatDefaultExtension = ".ppm";
    public const string FormatAlt1DefaultExtension = ".pbm";
    public const string FormatAlt2DefaultExtension = ".pgm";

    private PbmFormat() { }

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static PbmFormat Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => FormatName;

    /// <inheritdoc/>
    public string DefaultMimeType => FormatMimeType;

    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => PbmConstants.MimeTypes;

    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => PbmConstants.FileExtensions;

    /// <inheritdoc/>
    public string DefaultFileExtension => FormatDefaultExtension;

    /// <inheritdoc/>
    public PbmMetadata CreateDefaultFormatMetadata() => new();
}