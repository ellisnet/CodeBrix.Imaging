// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Tiff.Constants;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Tiff.Compression.Compressors; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.Compression.Compressors;

internal sealed class LzwCompressor : TiffBaseCompressor
{
    private TiffLzwEncoder lzwEncoder;

    public LzwCompressor(Stream output, MemoryAllocator allocator, int width, int bitsPerPixel, TiffPredictor predictor)
        : base(output, allocator, width, bitsPerPixel, predictor)
    {
    }

    /// <inheritdoc/>
    public override TiffCompression Method => TiffCompression.Lzw;

    /// <inheritdoc/>
    public override void Initialize(int rowsPerStrip) => this.lzwEncoder = new TiffLzwEncoder(this.Allocator);

    /// <inheritdoc/>
    public override void CompressStrip(Span<byte> rows, int height)
    {
        if (this.Predictor == TiffPredictor.Horizontal)
        {
            HorizontalPredictor.ApplyHorizontalPrediction(rows, this.BytesPerRow, this.BitsPerPixel);
        }

        this.lzwEncoder.Encode(rows, this.Output);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) => this.lzwEncoder?.Dispose();
}