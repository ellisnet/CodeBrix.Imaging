// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using CodeBrix.Imaging.IO;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Tiff.Compression.Decompressors; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.Compression.Decompressors;

/// <summary>
/// Class to handle cases where TIFF image data is compressed using PackBits compression.
/// </summary>
internal sealed class PackBitsTiffCompression : TiffBaseDecompressor
{
    private IMemoryOwner<byte> compressedDataMemory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackBitsTiffCompression" /> class.
    /// </summary>
    /// <param name="memoryAllocator">The memoryAllocator to use for buffer allocations.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="bitsPerPixel">The number of bits per pixel.</param>
    public PackBitsTiffCompression(MemoryAllocator memoryAllocator, int width, int bitsPerPixel)
        : base(memoryAllocator, width, bitsPerPixel)
    {
    }

    /// <inheritdoc/>
    protected override void Decompress(BufferedReadStream stream, int byteCount, int stripHeight, Span<byte> buffer)
    {
        if (this.compressedDataMemory == null)
        {
            this.compressedDataMemory = this.Allocator.Allocate<byte>(byteCount);
        }
        else if (this.compressedDataMemory.Length() < byteCount)
        {
            this.compressedDataMemory.Dispose();
            this.compressedDataMemory = this.Allocator.Allocate<byte>(byteCount);
        }

        // The pooled buffer can be larger than the requested byteCount, so bound every read by
        // byteCount rather than by the span length. Otherwise a malformed run reads stale bytes
        // left over from a previous use of the pooled buffer and decodes them into the image.
        var compressedData = this.compressedDataMemory.GetSpan().Slice(0, byteCount);

        if (stream.Read(compressedData, 0, byteCount) != byteCount)
        {
            TiffThrowHelper.ThrowImageFormatException("Tiff packbits compression error: not enough data.");
        }

        var compressedOffset = 0;
        var decompressedOffset = 0;

        while (compressedOffset < byteCount)
        {
            var headerByte = compressedData[compressedOffset];

            if (headerByte <= 127)
            {
                var literalOffset = compressedOffset + 1;
                var literalLength = compressedData[compressedOffset] + 1;

                if ((literalOffset + literalLength) > byteCount)
                {
                    TiffThrowHelper.ThrowImageFormatException("Tiff packbits compression error: not enough data.");
                }

                CheckDestinationCapacity(buffer, decompressedOffset, literalLength);

                compressedData.Slice(literalOffset, literalLength).CopyTo(buffer.Slice(decompressedOffset));

                compressedOffset += literalLength + 1;
                decompressedOffset += literalLength;
            }
            else if (headerByte == 0x80)
            {
                compressedOffset += 1;
            }
            else
            {
                // A repeat run is a header byte plus the value byte it repeats; both must be
                // present in the compressed data actually supplied.
                if (compressedOffset + 1 >= byteCount)
                {
                    TiffThrowHelper.ThrowImageFormatException("Tiff packbits compression error: not enough data.");
                }

                var repeatData = compressedData[compressedOffset + 1];
                var repeatLength = 257 - headerByte;

                CheckDestinationCapacity(buffer, decompressedOffset, repeatLength);

                ArrayCopyRepeat(repeatData, buffer, decompressedOffset, repeatLength);

                compressedOffset += 2;
                decompressedOffset += repeatLength;
            }
        }
    }

    /// <summary>
    /// Ensures a decoded run fits the destination strip buffer. A hostile TIFF can declare runs
    /// whose total length exceeds the buffer sized from the image dimensions; without this check
    /// the copy below runs off the end of the buffer.
    /// </summary>
    private static void CheckDestinationCapacity(Span<byte> buffer, int offset, int length)
    {
        if (offset < 0 || (long)offset + length > buffer.Length)
        {
            TiffThrowHelper.ThrowImageFormatException("Tiff packbits compression error: decoded data exceeds the strip buffer size.");
        }
    }

    private static void ArrayCopyRepeat(byte value, Span<byte> destinationArray, int destinationIndex, int length)
    {
        for (var i = 0; i < length; i++)
        {
            destinationArray[i + destinationIndex] = value;
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) => this.compressedDataMemory?.Dispose();
}