// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Processing.Processors.Quantization; //Was previously: namespace SixLabors.ImageSharp.Processing.Processors.Quantization;

/// <summary>
/// Enables the quantization of images to reduce the number of colors used in the image palette.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal class QuantizeProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly IQuantizer quantizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuantizeProcessor{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration which allows altering default behaviour or extending the library.</param>
    /// <param name="quantizer">The quantizer used to reduce the color palette.</param>
    /// <param name="source">The source <see cref="Image{TPixel}"/> for the current processor instance.</param>
    /// <param name="sourceRectangle">The source area to process for the current processor instance.</param>
    public QuantizeProcessor(Configuration configuration, IQuantizer quantizer, Image<TPixel> source, Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
    {
        Guard.NotNull(quantizer, nameof(quantizer));
        this.quantizer = quantizer;
    }

    /// <inheritdoc />
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        var interest = Rectangle.Intersect(source.Bounds(), this.SourceRectangle);

        var configuration = this.Configuration;
        using var frameQuantizer = this.quantizer.CreatePixelSpecificQuantizer<TPixel>(configuration);
        using var quantized = frameQuantizer.BuildPaletteAndQuantizeFrame(source, interest);

        var paletteSpan = quantized.Palette.Span;
        var offsetY = interest.Top;
        var offsetX = interest.Left;
        var sourceBuffer = source.PixelBuffer;

        for (var y = interest.Y; y < interest.Height; y++)
        {
            var row = sourceBuffer.DangerousGetRowSpan(y);
            var quantizedRow = quantized.DangerousGetRowSpan(y - offsetY);

            for (var x = interest.Left; x < interest.Right; x++)
            {
                row[x] = paletteSpan[quantizedRow[x - offsetX]];
            }
        }
    }
}