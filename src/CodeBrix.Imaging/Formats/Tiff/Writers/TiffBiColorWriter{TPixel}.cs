// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using CodeBrix.Imaging.Formats.Tiff.Compression;
using CodeBrix.Imaging.Formats.Tiff.Constants;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Metadata;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.Imaging.Processing;

namespace CodeBrix.Imaging.Formats.Tiff.Writers; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.Writers;

internal sealed class TiffBiColorWriter<TPixel> : TiffBaseColorWriter<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Image<TPixel> imageBlackWhite;

    private IMemoryOwner<byte> pixelsAsGray;

    private IMemoryOwner<byte> bitStrip;

    public TiffBiColorWriter(ImageFrame<TPixel> image, MemoryAllocator memoryAllocator, Configuration configuration, TiffEncoderEntriesCollector entriesCollector)
        : base(image, memoryAllocator, configuration, entriesCollector)
    {
        // Convert image to black and white.
        this.imageBlackWhite = new Image<TPixel>(configuration, new ImageMetadata(TiffFormat.Instance), new[] { image.Clone() });
        this.imageBlackWhite.Mutate(img => img.BinaryDither(KnownDitherings.FloydSteinberg));
    }

    /// <inheritdoc/>
    public override int BitsPerPixel => 1;

    /// <inheritdoc/>
    protected override void EncodeStrip(int y, int height, TiffBaseCompressor compressor)
    {
        var width = this.Image.Width;

        if (compressor.Method == TiffCompression.CcittGroup3Fax || compressor.Method == TiffCompression.Ccitt1D || compressor.Method == TiffCompression.CcittGroup4Fax)
        {
            // Special case for T4BitCompressor.
            var stripPixels = width * height;
            this.pixelsAsGray ??= this.MemoryAllocator.Allocate<byte>(stripPixels);
            this.imageBlackWhite.ProcessPixelRows(accessor =>
            {
                var pixelAsGraySpan = this.pixelsAsGray.GetSpan();
                var lastRow = y + height;
                var grayRowIdx = 0;
                for (var row = y; row < lastRow; row++)
                {
                    var pixelsBlackWhiteRow = accessor.GetRowSpan(row);
                    var pixelAsGrayRow = pixelAsGraySpan.Slice(grayRowIdx * width, width);
                    PixelOperations<TPixel>.Instance.ToL8Bytes(this.Configuration, pixelsBlackWhiteRow, pixelAsGrayRow, width);
                    grayRowIdx++;
                }

                compressor.CompressStrip(pixelAsGraySpan.Slice(0, stripPixels), height);
            });
        }
        else
        {
            // Write uncompressed image.
            var bytesPerStrip = this.BytesPerRow * height;
            this.bitStrip ??= this.MemoryAllocator.Allocate<byte>(bytesPerStrip);
            this.pixelsAsGray ??= this.MemoryAllocator.Allocate<byte>(width);
            var pixelAsGraySpan = this.pixelsAsGray.GetSpan();

            var rows = this.bitStrip.Slice(0, bytesPerStrip);
            rows.Clear();
            var blackWhiteBuffer = this.imageBlackWhite.Frames.RootFrame.PixelBuffer;

            var outputRowIdx = 0;
            var lastRow = y + height;
            for (var row = y; row < lastRow; row++)
            {
                var bitIndex = 0;
                var byteIndex = 0;
                var outputRow = rows.Slice(outputRowIdx * this.BytesPerRow);
                var pixelsBlackWhiteRow = blackWhiteBuffer.DangerousGetRowSpan(row);
                PixelOperations<TPixel>.Instance.ToL8Bytes(this.Configuration, pixelsBlackWhiteRow, pixelAsGraySpan, width);
                for (var x = 0; x < this.Image.Width; x++)
                {
                    var shift = 7 - bitIndex;
                    if (pixelAsGraySpan[x] == 255)
                    {
                        outputRow[byteIndex] |= (byte)(1 << shift);
                    }

                    bitIndex++;
                    if (bitIndex == 8)
                    {
                        byteIndex++;
                        bitIndex = 0;
                    }
                }

                outputRowIdx++;
            }

            compressor.CompressStrip(rows, height);
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        this.imageBlackWhite?.Dispose();
        this.pixelsAsGray?.Dispose();
        this.bitStrip?.Dispose();
    }
}