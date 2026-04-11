// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.IO;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Pbm; //Was previously: namespace SixLabors.ImageSharp.Formats.Pbm;

/// <summary>
/// Pixel encoding methods for the PBM binary encoding.
/// </summary>
internal class BinaryEncoder
{
    /// <summary>
    /// Decode pixels into the PBM binary encoding.
    /// </summary>
    /// <typeparam name="TPixel">The type of input pixel.</typeparam>
    /// <param name="configuration">The configuration.</param>
    /// <param name="stream">The bytestream to write to.</param>
    /// <param name="image">The input image.</param>
    /// <param name="colorType">The ColorType to use.</param>
    /// <param name="componentType">Data type of the pixles components.</param>
    /// <exception cref="InvalidImageContentException">
    /// Thrown if an invalid combination of setting is requested.
    /// </exception>
    public static void WritePixels<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image, PbmColorType colorType, PbmComponentType componentType)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (colorType == PbmColorType.Grayscale)
        {
            if (componentType == PbmComponentType.Byte)
            {
                WriteGrayscale(configuration, stream, image);
            }
            else
            {
                WriteWideGrayscale(configuration, stream, image);
            }
        }
        else if (colorType == PbmColorType.Rgb)
        {
            if (componentType == PbmComponentType.Byte)
            {
                WriteRgb(configuration, stream, image);
            }
            else
            {
                WriteWideRgb(configuration, stream, image);
            }
        }
        else
        {
            WriteBlackAndWhite(configuration, stream, image);
        }
    }

    private static void WriteGrayscale<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var width = image.Width;
        var height = image.Height;
        var pixelBuffer = image.PixelBuffer;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<byte>(width);
        var rowSpan = row.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToL8Bytes(
                configuration,
                pixelSpan,
                rowSpan,
                width);

            stream.Write(rowSpan);
        }
    }

    private static void WriteWideGrayscale<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 2;
        var width = image.Width;
        var height = image.Height;
        var pixelBuffer = image.PixelBuffer;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<byte>(width * bytesPerPixel);
        var rowSpan = row.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToL16Bytes(
                configuration,
                pixelSpan,
                rowSpan,
                width);

            stream.Write(rowSpan);
        }
    }

    private static void WriteRgb<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 3;
        var width = image.Width;
        var height = image.Height;
        var pixelBuffer = image.PixelBuffer;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<byte>(width * bytesPerPixel);
        var rowSpan = row.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToRgb24Bytes(
                configuration,
                pixelSpan,
                rowSpan,
                width);

            stream.Write(rowSpan);
        }
    }

    private static void WriteWideRgb<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int bytesPerPixel = 6;
        var width = image.Width;
        var height = image.Height;
        var pixelBuffer = image.PixelBuffer;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<byte>(width * bytesPerPixel);
        var rowSpan = row.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToRgb48Bytes(
                configuration,
                pixelSpan,
                rowSpan,
                width);

            stream.Write(rowSpan);
        }
    }

    private static void WriteBlackAndWhite<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var width = image.Width;
        var height = image.Height;
        var pixelBuffer = image.PixelBuffer;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<L8>(width);
        var rowSpan = row.GetSpan();

        var previousValue = 0;
        var startBit = 0;
        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToL8(
                configuration,
                pixelSpan,
                rowSpan);

            for (var x = 0; x < width;)
            {
                var value = previousValue;
                for (var i = startBit; i < 8; i++)
                {
                    if (rowSpan[x].PackedValue < 128)
                    {
                        value |= 0x80 >> i;
                    }

                    x++;
                    if (x == width)
                    {
                        previousValue = value;
                        startBit = (i + 1) & 7; // Round off to below 8.
                        break;
                    }
                }

                if (startBit == 0)
                {
                    stream.WriteByte((byte)value);
                    previousValue = 0;
                }
            }
        }
    }
}