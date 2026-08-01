// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Png; //Was previously: namespace SixLabors.ImageSharp.Formats.Png;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the png format.
/// </summary>
public sealed class PngFormat : IImageFormat<PngMetadata>
{
    /// <summary>
    /// The display name of the PNG format, as reported by <see cref="Name"/>.
    /// </summary>
    public const string FormatName = "PNG";

    /// <summary>
    /// The default MIME type of the PNG format, as reported by <see cref="DefaultMimeType"/>.
    /// </summary>
    public const string FormatMimeType = "image/png";

    /// <summary>
    /// The default file extension of the PNG format, as reported by <see cref="DefaultFileExtension"/>.
    /// </summary>
    public const string FormatDefaultExtension = ".png";

    private PngFormat() { }

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static PngFormat Instance { get; } = new PngFormat();

    /// <inheritdoc/>
    public string Name => FormatName;

    /// <inheritdoc/>
    public string DefaultMimeType => FormatMimeType;

    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => PngConstants.MimeTypes;

    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => PngConstants.FileExtensions;

    /// <inheritdoc/>
    public string DefaultFileExtension => FormatDefaultExtension;

    /// <inheritdoc/>
    public PngMetadata CreateDefaultFormatMetadata() => new PngMetadata();
}