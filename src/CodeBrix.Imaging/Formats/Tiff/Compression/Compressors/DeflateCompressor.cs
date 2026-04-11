// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Compression.Zlib;
using CodeBrix.Imaging.Formats.Tiff.Constants;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Tiff.Compression.Compressors; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.Compression.Compressors;

internal sealed class DeflateCompressor : TiffBaseCompressor
{
    private readonly DeflateCompressionLevel compressionLevel;

    private readonly MemoryStream memoryStream = new MemoryStream();

    public DeflateCompressor(Stream output, MemoryAllocator allocator, int width, int bitsPerPixel, TiffPredictor predictor, DeflateCompressionLevel compressionLevel)
        : base(output, allocator, width, bitsPerPixel, predictor)
        => this.compressionLevel = compressionLevel;

    /// <inheritdoc/>
    public override TiffCompression Method => TiffCompression.Deflate;

    /// <inheritdoc/>
    public override void Initialize(int rowsPerStrip)
    {
    }

    /// <inheritdoc/>
    public override void CompressStrip(Span<byte> rows, int height)
    {
        this.memoryStream.Seek(0, SeekOrigin.Begin);
        using (var stream = new ZlibDeflateStream(this.Allocator, this.memoryStream, this.compressionLevel))
        {
            if (this.Predictor == TiffPredictor.Horizontal)
            {
                HorizontalPredictor.ApplyHorizontalPrediction(rows, this.BytesPerRow, this.BitsPerPixel);
            }

            stream.Write(rows);
            stream.Flush();
        }

        var size = (int)this.memoryStream.Position;
        var buffer = this.memoryStream.GetBuffer();
        this.Output.Write(buffer, 0, size);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
    }
}