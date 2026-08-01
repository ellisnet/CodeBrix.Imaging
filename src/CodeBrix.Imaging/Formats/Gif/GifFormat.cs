// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Gif; //Was previously: namespace SixLabors.ImageSharp.Formats.Gif;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the gif format.
/// </summary>
public sealed class GifFormat : IImageFormat<GifMetadata, GifFrameMetadata>
{
    /// <summary>
    /// The display name of the GIF format, as reported by <see cref="Name"/>.
    /// </summary>
    public const string FormatName = "GIF";

    /// <summary>
    /// The default MIME type of the GIF format, as reported by <see cref="DefaultMimeType"/>.
    /// </summary>
    public const string FormatMimeType = "image/gif";

    /// <summary>
    /// The default file extension of the GIF format, as reported by <see cref="DefaultFileExtension"/>.
    /// </summary>
    public const string FormatDefaultExtension = ".gif";

    private GifFormat() { }

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static GifFormat Instance { get; } = new GifFormat();

    /// <inheritdoc/>
    public string Name => FormatName;

    /// <inheritdoc/>
    public string DefaultMimeType => FormatMimeType;

    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => GifConstants.MimeTypes;

    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => GifConstants.FileExtensions;

    /// <inheritdoc/>
    public string DefaultFileExtension => FormatDefaultExtension;

    /// <inheritdoc/>
    public GifMetadata CreateDefaultFormatMetadata() => new GifMetadata();

    /// <inheritdoc/>
    public GifFrameMetadata CreateDefaultFormatFrameMetadata() => new GifFrameMetadata();
}