// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using CodeBrix.Imaging.Formats.Jpeg.Components.Decoder;
using CodeBrix.Imaging.Formats.Jpeg.Components.Decoder.ColorConverters;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.Compression.Decompressors; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.Compression.Decompressors;

/// <summary>
/// Spectral converter for gray TIFF's which use the JPEG compression.
/// </summary>
/// <typeparam name="TPixel">The type of the pixel.</typeparam>
internal sealed class GrayJpegSpectralConverter<TPixel> : SpectralConverter<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GrayJpegSpectralConverter{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    public GrayJpegSpectralConverter(Configuration configuration)
        : base(configuration)
    {
    }

    /// <inheritdoc/>
    protected override JpegColorConverterBase GetColorConverter(JpegFrame frame, IRawJpegData jpegData) => JpegColorConverterBase.GetConverter(JpegColorSpace.Grayscale, frame.Precision);
}