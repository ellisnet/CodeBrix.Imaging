// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Webp; //Was previously: namespace SixLabors.ImageSharp.Formats.Webp;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the Webp format
/// </summary>
public sealed class WebpFormat : IImageFormat<WebpMetadata>
{
    public const string FormatName = "Webp";
    public const string FormatMimeType = "image/webp";
    public const string FormatDefaultExtension = ".webp";

    private WebpFormat() { }

    /// <summary>
    /// Gets the current instance.
    /// </summary>
    public static WebpFormat Instance { get; } = new WebpFormat();

    /// <inheritdoc/>
    public string Name => FormatName;

    /// <inheritdoc/>
    public string DefaultMimeType => FormatMimeType;

    /// <inheritdoc/>
    public IEnumerable<string> MimeTypes => WebpConstants.MimeTypes;

    /// <inheritdoc/>
    public IEnumerable<string> FileExtensions => WebpConstants.FileExtensions;

    /// <inheritdoc/>
    public string DefaultFileExtension => FormatDefaultExtension;

    /// <inheritdoc/>
    public WebpMetadata CreateDefaultFormatMetadata() => new WebpMetadata();
}