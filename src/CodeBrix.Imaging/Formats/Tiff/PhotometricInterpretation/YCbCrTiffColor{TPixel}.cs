// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using CodeBrix.Imaging.Formats.Tiff.Utils;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.PhotometricInterpretation; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.PhotometricInterpretation;

internal class YCbCrTiffColor<TPixel> : TiffBaseColorDecoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly MemoryAllocator memoryAllocator;

    private readonly YCbCrConverter converter;

    private readonly ushort[] ycbcrSubSampling;

    public YCbCrTiffColor(MemoryAllocator memoryAllocator, Rational[] referenceBlackAndWhite, Rational[] coefficients, ushort[] ycbcrSubSampling)
    {
        this.memoryAllocator = memoryAllocator;
        this.converter = new YCbCrConverter(referenceBlackAndWhite, coefficients);
        this.ycbcrSubSampling = ycbcrSubSampling;
    }

    /// <inheritdoc/>
    public override void Decode(ReadOnlySpan<byte> data, Buffer2D<TPixel> pixels, int left, int top, int width, int height)
    {
        var ycbcrData = data;
        if (this.ycbcrSubSampling != null && !(this.ycbcrSubSampling[0] == 1 && this.ycbcrSubSampling[1] == 1))
        {
            // 4 extra rows and columns for possible padding.
            var paddedWidth = width + 4;
            var paddedHeight = height + 4;
            var requiredBytes = paddedWidth * paddedHeight * 3;
            using var tmpBuffer = this.memoryAllocator.Allocate<byte>(requiredBytes);
            var tmpBufferSpan = tmpBuffer.GetSpan();
            ReverseChromaSubSampling(width, height, this.ycbcrSubSampling[0], this.ycbcrSubSampling[1], data, tmpBufferSpan);
            ycbcrData = tmpBufferSpan;
            this.DecodeYCbCrData(pixels, left, top, width, height, ycbcrData);
            return;
        }

        this.DecodeYCbCrData(pixels, left, top, width, height, ycbcrData);
    }

    private void DecodeYCbCrData(Buffer2D<TPixel> pixels, int left, int top, int width, int height, ReadOnlySpan<byte> ycbcrData)
    {
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
                var rgba = this.converter.ConvertToRgba32(ycbcrData[offset], ycbcrData[offset + 1], ycbcrData[offset + 2]);
                color.FromRgba32(rgba);
                pixelRow[x] = color;
                offset += 3;
            }

            offset += widthPadding * 3;
        }
    }

    private static void ReverseChromaSubSampling(int width, int height, int horizontalSubSampling, int verticalSubSampling, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        // If width and height are not multiples of ChromaSubsampleHoriz and ChromaSubsampleVert respectively,
        // then the source data will be padded.
        width += TiffUtils.PaddingToNextInteger(width, horizontalSubSampling);
        height += TiffUtils.PaddingToNextInteger(height, verticalSubSampling);
        var blockWidth = width / horizontalSubSampling;
        var blockHeight = height / verticalSubSampling;
        var cbCrOffsetInBlock = horizontalSubSampling * verticalSubSampling;
        var blockByteCount = cbCrOffsetInBlock + 2;

        for (var blockRow = blockHeight - 1; blockRow >= 0; blockRow--)
        {
            for (var blockCol = blockWidth - 1; blockCol >= 0; blockCol--)
            {
                var blockOffset = (blockRow * blockWidth) + blockCol;
                var blockData = source.Slice(blockOffset * blockByteCount, blockByteCount);
                var cr = blockData[cbCrOffsetInBlock + 1];
                var cb = blockData[cbCrOffsetInBlock];

                for (var row = verticalSubSampling - 1; row >= 0; row--)
                {
                    for (var col = horizontalSubSampling - 1; col >= 0; col--)
                    {
                        var offset = 3 * ((((blockRow * verticalSubSampling) + row) * width) + (blockCol * horizontalSubSampling) + col);
                        destination[offset + 2] = cr;
                        destination[offset + 1] = cb;
                        destination[offset] = blockData[(row * horizontalSubSampling) + col];
                    }
                }
            }
        }
    }
}