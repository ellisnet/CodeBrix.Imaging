// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using CodeBrix.Imaging.Formats.Tiff.Utils;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.PhotometricInterpretation; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

/// <summary>
/// Implements the 'RGB' photometric interpretation with an alpha channel and with 'Planar' layout for each color channel with 16 bit.
/// </summary>
internal class Rgba16PlanarTiffColor<TPixel> : TiffBasePlanarColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly bool isBigEndian;

    private readonly TiffExtraSampleType? extraSamplesType;

    /// <summary>
    /// Initializes a new instance of the <see cref="Rgba16PlanarTiffColor{TPixel}" /> class.
    /// </summary>
    /// <param name="extraSamplesType">The extra samples type.</param>
    /// <param name="isBigEndian">If set to <c>true</c> decodes the pixel data as big endian, otherwise as little endian.</param>
    public Rgba16PlanarTiffColor(TiffExtraSampleType? extraSamplesType, bool isBigEndian)
    {
        this.extraSamplesType = extraSamplesType;
        this.isBigEndian = isBigEndian;
    }

    /// <inheritdoc/>
    public override void Decode(IMemoryOwner<byte>[] data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        // Note: due to an issue with netcore 2.1 and default values and unpredictable behavior with those,
        // we define our own defaults as a workaround. See: https://github.com/dotnet/runtime/issues/55623
        var rgba = TiffUtils.Rgba64Default;
        var color = default(TPixel);
        color.FromScaledVector4(TiffUtils.Vector4Default);

        var redData = data[0].GetSpan();
        var greenData = data[1].GetSpan();
        var blueData = data[2].GetSpan();
        var alphaData = data[3].GetSpan();

        var hasAssociatedAlpha = this.extraSamplesType.HasValue && this.extraSamplesType == TiffExtraSampleType.AssociatedAlphaData;
        var offset = 0;
        for (var y = top; y < top + height; y++)
        {
            var pixelRow = pixels.DangerousGetRowSpan(y).Slice(left, width);
            if (this.isBigEndian)
            {
                for (var x = 0; x < pixelRow.Length; x++)
                {
                    ulong r = TiffUtils.ConvertToUShortBigEndian(redData.Slice(offset, 2));
                    ulong g = TiffUtils.ConvertToUShortBigEndian(greenData.Slice(offset, 2));
                    ulong b = TiffUtils.ConvertToUShortBigEndian(blueData.Slice(offset, 2));
                    ulong a = TiffUtils.ConvertToUShortBigEndian(alphaData.Slice(offset, 2));

                    offset += 2;

                    pixelRow[x] = hasAssociatedAlpha ?
                        TiffUtils.ColorFromRgba64Premultiplied(rgba, r, g, b, a, color) :
                        TiffUtils.ColorFromRgba64(rgba, r, g, b, a, color);
                }
            }
            else
            {
                for (var x = 0; x < pixelRow.Length; x++)
                {
                    ulong r = TiffUtils.ConvertToUShortLittleEndian(redData.Slice(offset, 2));
                    ulong g = TiffUtils.ConvertToUShortLittleEndian(greenData.Slice(offset, 2));
                    ulong b = TiffUtils.ConvertToUShortLittleEndian(blueData.Slice(offset, 2));
                    ulong a = TiffUtils.ConvertToUShortLittleEndian(alphaData.Slice(offset, 2));

                    offset += 2;

                    pixelRow[x] = hasAssociatedAlpha ?
                        TiffUtils.ColorFromRgba64Premultiplied(rgba, r, g, b, a, color) :
                        TiffUtils.ColorFromRgba64(rgba, r, g, b, a, color);
                }
            }
        }
    }
}