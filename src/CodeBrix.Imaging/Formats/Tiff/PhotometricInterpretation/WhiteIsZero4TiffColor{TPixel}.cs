// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.PhotometricInterpretation; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Implements the 'WhiteIsZero' photometric interpretation (optimized for 4-bit grayscale images).
/// </summary>
internal class WhiteIsZero4TiffColor<TPixel> : TiffBaseColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <inheritdoc/>
    public override void Decode(ReadOnlySpan<byte> data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        var color = default(TPixel);

        var offset = 0;
        var isOddWidth = (width & 1) == 1;

        var l8 = default(L8);
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width - 1;)
            {
                var byteData = data[offset++];

                var intensity1 = (byte)((15 - ((byteData & 0xF0) >> 4)) * 17);
                l8.PackedValue = intensity1;
                color.FromL8(l8);

                pixels[x++, y] = color;

                var intensity2 = (byte)((15 - (byteData & 0x0F)) * 17);
                l8.PackedValue = intensity2;
                color.FromL8(l8);

                pixels[x++, y] = color;
            }

            if (isOddWidth)
            {
                var byteData = data[offset++];

                var intensity1 = (byte)((15 - ((byteData & 0xF0) >> 4)) * 17);
                l8.PackedValue = intensity1;
                color.FromL8(l8);

                pixels[left + width - 1, y] = color;
            }
        }
    }
}