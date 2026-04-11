// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using CodeBrix.Imaging.Formats.Tiff.Utils;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.PhotometricInterpretation; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Implements the 'PaletteTiffColor' photometric interpretation (for all bit depths).
/// </summary>
internal class PaletteTiffColor<TPixel> : TiffBaseColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly ushort bitsPerSample0;

    private readonly TPixel[] palette;

    /// <param name="bitsPerSample">The number of bits per sample for each pixel.</param>
    /// <param name="colorMap">The RGB color lookup table to use for decoding the image.</param>
    public PaletteTiffColor(TiffBitsPerSample bitsPerSample, ushort[] colorMap)
    {
        this.bitsPerSample0 = bitsPerSample.Channel0;
        var colorCount = 1 << this.bitsPerSample0;
        this.palette = GeneratePalette(colorMap, colorCount);
    }

    /// <inheritdoc/>
    public override void Decode(ReadOnlySpan<byte> data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        var bitReader = new BitReader(data);

        for (var y = top; y < top + height; y++)
        {
            var pixelRow = pixels.DangerousGetRowSpan(y).Slice(left, width);
            for (var x = 0; x < pixelRow.Length; x++)
            {
                var index = bitReader.ReadBits(this.bitsPerSample0);
                pixelRow[x] = this.palette[index];
            }

            bitReader.NextRow();
        }
    }

    private static TPixel[] GeneratePalette(ushort[] colorMap, int colorCount)
    {
        var palette = new TPixel[colorCount];

        const int rOffset = 0;
        var gOffset = colorCount;
        var bOffset = colorCount * 2;

        for (var i = 0; i < palette.Length; i++)
        {
            var r = colorMap[rOffset + i] / 65535F;
            var g = colorMap[gOffset + i] / 65535F;
            var b = colorMap[bOffset + i] / 65535F;
            palette[i].FromScaledVector4(new Vector4(r, g, b, 1.0f));
        }

        return palette;
    }
}