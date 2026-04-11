// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using CodeBrix.Imaging.Formats.Tiff.Compression;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Tiff.Writers; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.Writers;

/// <summary>
/// The base class for composite color types: 8-bit gray, 24-bit RGB (4-bit gray, 16-bit (565/555) RGB, 32-bit RGB, CMYK, YCbCr).
/// </summary>
internal abstract class TiffCompositeColorWriter<TPixel> : TiffBaseColorWriter<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private IMemoryOwner<byte> rowBuffer;

    protected TiffCompositeColorWriter(ImageFrame<TPixel> image, MemoryAllocator memoryAllocator, Configuration configuration, TiffEncoderEntriesCollector entriesCollector)
        : base(image, memoryAllocator, configuration, entriesCollector)
    {
    }

    protected override void EncodeStrip(int y, int height, TiffBaseCompressor compressor)
    {
        if (this.rowBuffer == null)
        {
            this.rowBuffer = this.MemoryAllocator.Allocate<byte>(this.BytesPerRow * height);
        }

        this.rowBuffer.Clear();

        var outputRowSpan = this.rowBuffer.GetSpan().Slice(0, this.BytesPerRow * height);

        var width = this.Image.Width;
        using var stripPixelBuffer = this.MemoryAllocator.Allocate<TPixel>(height * width);
        var stripPixels = stripPixelBuffer.GetSpan();
        var lastRow = y + height;
        var stripPixelsRowIdx = 0;
        for (var row = y; row < lastRow; row++)
        {
            var stripPixelsRow = this.Image.PixelBuffer.DangerousGetRowSpan(row);
            stripPixelsRow.CopyTo(stripPixels.Slice(stripPixelsRowIdx * width, width));
            stripPixelsRowIdx++;
        }

        this.EncodePixels(stripPixels, outputRowSpan);
        compressor.CompressStrip(outputRowSpan, height);
    }

    protected abstract void EncodePixels(Span<TPixel> pixels, Span<byte> buffer);

    /// <inheritdoc />
    protected override void Dispose(bool disposing) => this.rowBuffer?.Dispose();
}