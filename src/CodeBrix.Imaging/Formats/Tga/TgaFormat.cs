// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Tga; //Was previously: namespace SixLabors.ImageSharp.Formats.Tga;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the tga format.
/// </summary>
public sealed class TgaFormat : IImageFormat<TgaMetadata>
{
    /// <summary>
    /// The display name of the TGA format, as reported by <see cref="Name"/>.
    /// </summary>
    public const string FormatName = "TGA";

    /// <summary>
    /// The default MIME type of the TGA format, as reported by <see cref="DefaultMimeType"/>.
    /// </summary>
    public const string FormatMimeType = "image/x-tga";

    /// <summary>
    /// The alternate MIME type recognised for the TGA format.
    /// </summary>
    public const string FormatAltMimeType = "image/tga";

    /// <summary>
    /// The default file extension of the TGA format, as reported by <see cref="DefaultFileExtension"/>.
    /// </summary>
    public const string FormatDefaultExtension = ".tga";

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static TgaFormat Instance { get; } = new TgaFormat();

    /// <inheritdoc/>
    public string Name => FormatName;

    /// <inheritdoc/>
    public string DefaultMimeType => FormatMimeType;

    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => TgaConstants.MimeTypes;

    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => TgaConstants.FileExtensions;

    /// <inheritdoc/>
    public string DefaultFileExtension => FormatDefaultExtension;

    /// <inheritdoc/>
    public TgaMetadata CreateDefaultFormatMetadata() => new TgaMetadata();
}