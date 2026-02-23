// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using CodeBrix.Imaging.Formats;
using CodeBrix.Imaging.Metadata;

namespace CodeBrix.Imaging; //Was previously: namespace SixLabors.ImageSharp;

/// <summary>
/// Encapsulates properties that describe basic image information including dimensions, pixel type information
/// and additional metadata.
/// </summary>
public interface IImageInfo
{
    /// <summary>
    /// Gets information about the image pixels.
    /// </summary>
    PixelTypeInfo PixelType { get; }

    /// <summary>
    /// Gets the width.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets the metadata of the image.
    /// </summary>
    ImageMetadata Metadata { get; }

    /// <summary>
    /// Gets the format of the image represented by this instance.
    /// </summary>
    IImageFormat Format { get; }

    /// <summary>
    /// Gets the horizontal resolution of the image.
    /// </summary>
    double HorizontalResolution { get; }

    /// <summary>
    /// Gets the vertical resolution of the image.
    /// </summary>
    double VerticalResolution { get; }
}
