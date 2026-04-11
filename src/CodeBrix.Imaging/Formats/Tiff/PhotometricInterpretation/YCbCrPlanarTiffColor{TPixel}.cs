// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using CodeBrix.Imaging.Formats.Tiff.Utils;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.PhotometricInterpretation; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

internal class YCbCrPlanarTiffColor<TPixel> : TiffBasePlanarColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly YCbCrConverter converter;

    private readonly ushort[] ycbcrSubSampling;

    public YCbCrPlanarTiffColor(Rational[] referenceBlackAndWhite, Rational[] coefficients, ushort[] ycbcrSubSampling)
    {
        this.converter = new YCbCrConverter(referenceBlackAndWhite, coefficients);
        this.ycbcrSubSampling = ycbcrSubSampling;
    }

    /// <inheritdoc/>
    public override void Decode(IMemoryOwner<byte>[] data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        var yData = data[0].GetSpan();
        var cbData = data[1].GetSpan();
        var crData = data[2].GetSpan();

        if (this.ycbcrSubSampling != null && !(this.ycbcrSubSampling[0] == 1 && this.ycbcrSubSampling[1] == 1))
        {
            ReverseChromaSubSampling(width, height, this.ycbcrSubSampling[0], this.ycbcrSubSampling[1], cbData, crData);
        }

        var color = default(TPixel);
        var offset = 0;
        var widthPadding = 0;
        if (this.ycbcrSubSampling != null)
        {
            // Round to the next integer multiple of horizontalSubSampling.
            widthPadding = TiffUtils.PaddingToNextInteger(width, this.ycbcrSubSampling[0]);
        }

        for (var y = top; y < top + height; y++)
        {
            var pixelRow = pixels.DangerousGetRowSpan(y).Slice(left, width);
            for (var x = 0; x < pixelRow.Length; x++)
            {
                var rgba = this.converter.ConvertToRgba32(yData[offset], cbData[offset], crData[offset]);
                color.FromRgba32(rgba);
                pixelRow[x] = color;
                offset++;
            }

            offset += widthPadding;
        }
    }

    private static void ReverseChromaSubSampling(int width, int height, int horizontalSubSampling, int verticalSubSampling, Span<byte> planarCb, Span<byte> planarCr)
    {
        // If width and height are not multiples of ChromaSubsampleHoriz and ChromaSubsampleVert respectively,
        // then the source data will be padded.
        width += TiffUtils.PaddingToNextInteger(width, horizontalSubSampling);
        height += TiffUtils.PaddingToNextInteger(height, verticalSubSampling);

        for (var row = height - 1; row >= 0; row--)
        {
            for (var col = width - 1; col >= 0; col--)
            {
                var offset = (row * width) + col;
                var subSampleOffset = (row / verticalSubSampling * (width / horizontalSubSampling)) + (col / horizontalSubSampling);
                planarCb[offset] = planarCb[subSampleOffset];
                planarCr[offset] = planarCr[subSampleOffset];
            }
        }
    }
}