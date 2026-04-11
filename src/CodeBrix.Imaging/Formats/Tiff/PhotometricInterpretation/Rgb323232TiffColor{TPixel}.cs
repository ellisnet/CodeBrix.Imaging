// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Formats.Tiff.Utils;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.PhotometricInterpretation; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Implements the 'RGB' photometric interpretation with 32 bits for each channel.
/// </summary>
internal class Rgb323232TiffColor<TPixel> : TiffBaseColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly bool isBigEndian;

    /// <summary>
    /// Initializes a new instance of the <see cref="Rgb323232TiffColor{TPixel}" /> class.
    /// </summary>
    /// <param name="isBigEndian">if set to <c>true</c> decodes the pixel data as big endian, otherwise as little endian.</param>
    public Rgb323232TiffColor(bool isBigEndian) => this.isBigEndian = isBigEndian;

    /// <inheritdoc/>
    public override void Decode(ReadOnlySpan<byte> data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        // Note: due to an issue with netcore 2.1 and default values and unpredictable behavior with those,
        // we define our own defaults as a workaround. See: https://github.com/dotnet/runtime/issues/55623
        var color = default(TPixel);
        color.FromScaledVector4(TiffUtils.Vector4Default);
        var offset = 0;

        for (var y = top; y < top + height; y++)
        {
            var pixelRow = pixels.DangerousGetRowSpan(y).Slice(left, width);

            if (this.isBigEndian)
            {
                for (var x = 0; x < pixelRow.Length; x++)
                {
                    ulong r = TiffUtils.ConvertToUIntBigEndian(data.Slice(offset, 4));
                    offset += 4;

                    ulong g = TiffUtils.ConvertToUIntBigEndian(data.Slice(offset, 4));
                    offset += 4;

                    ulong b = TiffUtils.ConvertToUIntBigEndian(data.Slice(offset, 4));
                    offset += 4;

                    pixelRow[x] = TiffUtils.ColorScaleTo32Bit(r, g, b, color);
                }
            }
            else
            {
                for (var x = 0; x < pixelRow.Length; x++)
                {
                    ulong r = TiffUtils.ConvertToUIntLittleEndian(data.Slice(offset, 4));
                    offset += 4;

                    ulong g = TiffUtils.ConvertToUIntLittleEndian(data.Slice(offset, 4));
                    offset += 4;

                    ulong b = TiffUtils.ConvertToUIntLittleEndian(data.Slice(offset, 4));
                    offset += 4;

                    pixelRow[x] = TiffUtils.ColorScaleTo32Bit(r, g, b, color);
                }
            }
        }
    }
}