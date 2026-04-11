// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using CodeBrix.Imaging.IO;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Pbm; //Was previously: namespace SixLabors.ImageSharp.Formats.Pbm;

/// <summary>
/// Pixel decoding methods for the PBM binary encoding.
/// </summary>
internal class BinaryDecoder
{
    private static L8 white = new(255);
    private static L8 black = new(0);

    /// <summary>
    /// Decode the specified pixels.
    /// </summary>
    /// <typeparam name="TPixel">The type of pixel to encode to.</typeparam>
    /// <param name="configuration">The configuration.</param>
    /// <param name="pixels">The pixel array to encode into.</param>
    /// <param name="stream">The stream to read the data from.</param>
    /// <param name="colorType">The ColorType to decode.</param>
    /// <param name="componentType">Data type of the pixles components.</param>
    /// <exception cref="InvalidImageContentException">
    /// Thrown if an invalid combination of setting is requested.
    /// </exception>
    public static void Process<TPixel>(Configuration configuration, Buffer2D<TPixel> pixels, BufferedReadStream stream, PbmColorType colorType, PbmComponentType componentType)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (colorType == PbmColorType.Grayscale)
        {
            if (componentType == PbmComponentType.Byte)
            {
                ProcessGrayscale(configuration, pixels, stream);
            }
            else
            {
                ProcessWideGrayscale(configuration, pixels, stream);
            }
        }
        else if (colorType == PbmColorType.Rgb)
        {
            if (componentType == PbmComponentType.Byte)
            {
                ProcessRgb(configuration, pixels, stream);
            }
            else
            {
                ProcessWideRgb(configuration, pixels, stream);
            }
        }
        else
        {
            ProcessBlackAndWhite(configuration, pixels, stream);
        }
    }

    private static void ProcessGrayscale<TPixel>(Configuration configuration, Buffer2D<TPixel> pixels, BufferedReadStream stream)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 1;
        var width = pixels.Width;
        var height = pixels.Height;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<byte>(width * bytesPerPixel);
        var rowSpan = row.GetSpan();

        for (var y = 0; y < height; y++)
        {
            if (stream.Read(rowSpan) < rowSpan.Length)
            {
                return;
            }

            var pixelSpan = pixels.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.FromL8Bytes(
                configuration,
                rowSpan,
                pixelSpan,
                width);
        }
    }

    private static void ProcessWideGrayscale<TPixel>(Configuration configuration, Buffer2D<TPixel> pixels, BufferedReadStream stream)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 2;
        var width = pixels.Width;
        var height = pixels.Height;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<byte>(width * bytesPerPixel);
        var rowSpan = row.GetSpan();

        for (var y = 0; y < height; y++)
        {
            if (stream.Read(rowSpan) < rowSpan.Length)
            {
                return;
            }

            var pixelSpan = pixels.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.FromL16Bytes(
                configuration,
                rowSpan,
                pixelSpan,
                width);
        }
    }

    private static void ProcessRgb<TPixel>(Configuration configuration, Buffer2D<TPixel> pixels, BufferedReadStream stream)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 3;
        var width = pixels.Width;
        var height = pixels.Height;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<byte>(width * bytesPerPixel);
        var rowSpan = row.GetSpan();

        for (var y = 0; y < height; y++)
        {
            if (stream.Read(rowSpan) < rowSpan.Length)
            {
                return;
            }

            var pixelSpan = pixels.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.FromRgb24Bytes(
                configuration,
                rowSpan,
                pixelSpan,
                width);
        }
    }

    private static void ProcessWideRgb<TPixel>(Configuration configuration, Buffer2D<TPixel> pixels, BufferedReadStream stream)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 6;
        var width = pixels.Width;
        var height = pixels.Height;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<byte>(width * bytesPerPixel);
        var rowSpan = row.GetSpan();

        for (var y = 0; y < height; y++)
        {
            if (stream.Read(rowSpan) < rowSpan.Length)
            {
                return;
            }

            var pixelSpan = pixels.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.FromRgb48Bytes(
                configuration,
                rowSpan,
                pixelSpan,
                width);
        }
    }

    private static void ProcessBlackAndWhite<TPixel>(Configuration configuration, Buffer2D<TPixel> pixels, BufferedReadStream stream)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var width = pixels.Width;
        var height = pixels.Height;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<L8>(width);
        var rowSpan = row.GetSpan();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width;)
            {
                var raw = stream.ReadByte();
                if (raw < 0)
                {
                    return;
                }

                var stopBit = Math.Min(8, width - x);
                for (var bit = 0; bit < stopBit; bit++)
                {
                    var bitValue = (raw & (0x80 >> bit)) != 0;
                    rowSpan[x] = bitValue ? black : white;
                    x++;
                }
            }

            var pixelSpan = pixels.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.FromL8(
                configuration,
                rowSpan,
                pixelSpan);
        }
    }
}