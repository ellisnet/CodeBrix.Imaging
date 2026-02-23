// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using CodeBrix.Imaging.Formats;
using CodeBrix.Imaging.Metadata;
using System;

namespace CodeBrix.Imaging; //Was previously: namespace SixLabors.ImageSharp;

/// <summary>
/// Contains information about the image including dimensions, pixel type information and additional metadata
/// </summary>
internal sealed class ImageInfo : IImageInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImageInfo"/> class.
    /// </summary>
    /// <param name="pixelType">The image pixel type information.</param>
    /// <param name="width">The width of the image in pixels.</param>
    /// <param name="height">The height of the image in pixels.</param>
    /// <param name="metadata">The image metadata.</param>
    /// <param name="format">Expected format of the image.</param>
    public ImageInfo(PixelTypeInfo pixelType, int width, int height, ImageMetadata metadata, IImageFormat format)
    {
        this.PixelType = pixelType;
        this.Width = width;
        this.Height = height;
        this.Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <inheritdoc />
    public PixelTypeInfo PixelType { get; }

    /// <inheritdoc />
    public int Width { get; }

    /// <inheritdoc />
    public int Height { get; }

    /// <inheritdoc />
    public ImageMetadata Metadata { get; }

    /// <inheritdoc />
    public IImageFormat Format => this.Metadata.ExpectedFormat;

    /// <inheritdoc />
    public double HorizontalResolution => Metadata?.HorizontalResolution
                                          ?? throw new InvalidOperationException(
                                              $"{nameof(HorizontalResolution)} value not available for this image.");

    /// <inheritdoc />
    public double VerticalResolution => Metadata?.VerticalResolution
                                        ?? throw new InvalidOperationException(
                                            $"{nameof(VerticalResolution)} value not available for this image.");
}