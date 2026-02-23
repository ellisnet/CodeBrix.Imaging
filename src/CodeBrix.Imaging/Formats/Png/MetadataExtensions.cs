// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using CodeBrix.Imaging.Formats.Png;
using CodeBrix.Imaging.Metadata;

namespace CodeBrix.Imaging; //Was previously: namespace SixLabors.ImageSharp;

/// <summary>
/// Extension methods for the <see cref="ImageMetadata"/> type.
/// </summary>
public static partial class MetadataExtensions
{
    /// <summary>
    /// Gets the png format specific metadata for the image.
    /// </summary>
    /// <param name="metadata">The metadata this method extends.</param>
    /// <returns>The <see cref="PngMetadata"/>.</returns>
    public static PngMetadata GetPngMetadata(this ImageMetadata metadata) => metadata.GetFormatMetadata(PngFormat.Instance);
}