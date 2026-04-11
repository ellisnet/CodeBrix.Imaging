// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Formats.Tiff.Utils;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.PhotometricInterpretation; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Implements the 'WhiteIsZero' photometric interpretation for 16-bit grayscale images.
/// </summary>
internal class WhiteIsZero16TiffColor<TPixel> : TiffBaseColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly bool isBigEndian;

    /// <summary>
    /// Initializes a new instance of the <see cref="WhiteIsZero16TiffColor{TPixel}" /> class.
    /// </summary>
    /// <param name="isBigEndian">if set to <c>true</c> decodes the pixel data as big endian, otherwise as little endian.</param>
    public WhiteIsZero16TiffColor(bool isBigEndian) => this.isBigEndian = isBigEndian;

    /// <inheritdoc/>
    public override void Decode(ReadOnlySpan<byte> data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        // Note: due to an issue with netcore 2.1 and default values and unpredictable behavior with those,
        // we define our own defaults as a workaround. See: https://github.com/dotnet/runtime/issues/55623
        var l16 = TiffUtils.L16Default;
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
                    var intensity = (ushort)(ushort.MaxValue - TiffUtils.ConvertToUShortBigEndian(data.Slice(offset, 2)));
                    offset += 2;

                    pixelRow[x] = TiffUtils.ColorFromL16(l16, intensity, color);
                }
            }
            else
            {
                for (var x = 0; x < pixelRow.Length; x++)
                {
                    var intensity = (ushort)(ushort.MaxValue - TiffUtils.ConvertToUShortLittleEndian(data.Slice(offset, 2)));
                    offset += 2;

                    pixelRow[x] = TiffUtils.ColorFromL16(l16, intensity, color);
                }
            }
        }
    }
}