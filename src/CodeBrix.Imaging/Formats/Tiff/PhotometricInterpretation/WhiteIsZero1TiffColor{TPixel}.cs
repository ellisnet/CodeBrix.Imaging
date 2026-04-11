// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.PhotometricInterpretation; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Implements the 'WhiteIsZero' photometric interpretation (optimized for bilevel images).
/// </summary>
internal class WhiteIsZero1TiffColor<TPixel> : TiffBaseColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <inheritdoc/>
    public override void Decode(ReadOnlySpan<byte> data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        var color = default(TPixel);

        var offset = 0;

        var black = Color.Black;
        var white = Color.White;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x += 8)
            {
                var b = data[offset++];
                var maxShift = Math.Min(left + width - x, 8);

                for (var shift = 0; shift < maxShift; shift++)
                {
                    var bit = (b >> (7 - shift)) & 1;

                    color.FromRgba32(bit == 0 ? white : black);

                    pixels[x + shift, y] = color;
                }
            }
        }
    }
}