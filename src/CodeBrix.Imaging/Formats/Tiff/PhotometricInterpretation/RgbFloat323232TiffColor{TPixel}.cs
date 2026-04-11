// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using CodeBrix.Imaging.Formats.Tiff.Utils;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.PhotometricInterpretation; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Implements the 'RGB' photometric interpretation with 32 bits for each channel.
/// </summary>
internal class RgbFloat323232TiffColor<TPixel> : TiffBaseColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly bool isBigEndian;

    /// <summary>
    /// Initializes a new instance of the <see cref="RgbFloat323232TiffColor{TPixel}" /> class.
    /// </summary>
    /// <param name="isBigEndian">if set to <c>true</c> decodes the pixel data as big endian, otherwise as little endian.</param>
    public RgbFloat323232TiffColor(bool isBigEndian) => this.isBigEndian = isBigEndian;

    /// <inheritdoc/>
    public override void Decode(ReadOnlySpan<byte> data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        // Note: due to an issue with netcore 2.1 and default values and unpredictable behavior with those,
        // we define our own defaults as a workaround. See: https://github.com/dotnet/runtime/issues/55623
        var color = default(TPixel);
        color.FromScaledVector4(TiffUtils.Vector4Default);
        var offset = 0;
        var buffer = new byte[4];

        for (var y = top; y < top + height; y++)
        {
            var pixelRow = pixels.DangerousGetRowSpan(y).Slice(left, width);

            if (this.isBigEndian)
            {
                for (var x = 0; x < pixelRow.Length; x++)
                {
                    data.Slice(offset, 4).CopyTo(buffer);
                    Array.Reverse(buffer);
                    var r = BitConverter.ToSingle(buffer, 0);
                    offset += 4;

                    data.Slice(offset, 4).CopyTo(buffer);
                    Array.Reverse(buffer);
                    var g = BitConverter.ToSingle(buffer, 0);
                    offset += 4;

                    data.Slice(offset, 4).CopyTo(buffer);
                    Array.Reverse(buffer);
                    var b = BitConverter.ToSingle(buffer, 0);
                    offset += 4;

                    var colorVector = new Vector4(r, g, b, 1.0f);
                    color.FromScaledVector4(colorVector);
                    pixelRow[x] = color;
                }
            }
            else
            {
                for (var x = 0; x < pixelRow.Length; x++)
                {
                    data.Slice(offset, 4).CopyTo(buffer);
                    var r = BitConverter.ToSingle(buffer, 0);
                    offset += 4;

                    data.Slice(offset, 4).CopyTo(buffer);
                    var g = BitConverter.ToSingle(buffer, 0);
                    offset += 4;

                    data.Slice(offset, 4).CopyTo(buffer);
                    var b = BitConverter.ToSingle(buffer, 0);
                    offset += 4;

                    var colorVector = new Vector4(r, g, b, 1.0f);
                    color.FromScaledVector4(colorVector);
                    pixelRow[x] = color;
                }
            }
        }
    }
}