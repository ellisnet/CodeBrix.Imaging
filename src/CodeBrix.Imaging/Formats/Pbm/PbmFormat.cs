// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Pbm; //Was previously: namespace SixLabors.ImageSharp.Formats.Pbm;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the PBM format.
/// </summary>
public sealed class PbmFormat : IImageFormat<PbmMetadata>
{
    /// <summary>
    /// The display name of the PBM format, as reported by <see cref="Name"/>.
    /// </summary>
    public const string FormatName = "PBM";

    /// <summary>
    /// The default MIME type of the PBM format, as reported by <see cref="DefaultMimeType"/>.
    /// </summary>
    public const string FormatMimeType = "image/x-portable-pixmap";

    /// <summary>
    /// The default file extension of the PBM format, as reported by <see cref="DefaultFileExtension"/>.
    /// </summary>
    public const string FormatDefaultExtension = ".ppm";

    /// <summary>
    /// An alternate file extension recognised for the PBM format (portable bitmap).
    /// </summary>
    public const string FormatAlt1DefaultExtension = ".pbm";

    /// <summary>
    /// An alternate file extension recognised for the PBM format (portable graymap).
    /// </summary>
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