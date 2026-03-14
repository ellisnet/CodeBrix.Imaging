// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace CodeBrix.Imaging.Formats.Tga; //Was previously: namespace SixLabors.ImageSharp.Formats.Tga;

/// <summary>
/// Registers the image encoders, decoders and mime type detectors for the tga format.
/// </summary>
public sealed class TgaFormat : IImageFormat<TgaMetadata>
{
    public const string FormatName = "TGA";
    public const string FormatMimeType = "image/x-tga";
    public const string FormatAltMimeType = "image/tga";
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