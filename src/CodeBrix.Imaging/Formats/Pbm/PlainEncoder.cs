// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Pbm; //Was previously: namespace SixLabors.ImageSharp.Formats.Pbm;

/// <summary>
/// Pixel encoding methods for the PBM plain encoding.
/// </summary>
internal class PlainEncoder
{
    private const byte NewLine = 0x0a;
    private const byte Space = 0x20;
    private const byte Zero = 0x30;
    private const byte One = 0x31;

    private const int MaxCharsPerPixelBlackAndWhite = 2;
    private const int MaxCharsPerPixelGrayscale = 4;
    private const int MaxCharsPerPixelGrayscaleWide = 6;
    private const int MaxCharsPerPixelRgb = 4 * 3;
    private const int MaxCharsPerPixelRgbWide = 6 * 3;

    private static readonly StandardFormat DecimalFormat = StandardFormat.Parse("D");

    /// <summary>
    /// Decode pixels into the PBM plain encoding.
    /// </summary>
    /// <typeparam name="TPixel">The type of input pixel.</typeparam>
    /// <param name="configuration">The configuration.</param>
    /// <param name="stream">The bytestream to write to.</param>
    /// <param name="image">The input image.</param>
    /// <param name="colorType">The ColorType to use.</param>
    /// <param name="componentType">Data type of the pixles components.</param>
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

        // Write EOF indicator, as some encoders expect it.
        stream.WriteByte(Space);
    }

    private static void WriteGrayscale<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var width = image.Width;
        var height = image.Height;
        var pixelBuffer = image.PixelBuffer;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<L8>(width);
        var rowSpan = row.GetSpan();
        using var plainMemory = allocator.Allocate<byte>(width * MaxCharsPerPixelGrayscale);
        var plainSpan = plainMemory.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.ToL8(
                configuration,
                pixelSpan,
                rowSpan);

            var written = 0;
            for (var x = 0; x < width; x++)
            {
                Utf8Formatter.TryFormat(rowSpan[x].PackedValue, plainSpan.Slice(written), out var bytesWritten, DecimalFormat);
                written += bytesWritten;
                plainSpan[written++] = Space;
            }

            plainSpan[written - 1] = NewLine;
            stream.Write(plainSpan, 0, written);
        }
    }

    private static void WriteWideGrayscale<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var width = image.Width;
        var height = image.Height;
        var pixelBuffer = image.PixelBuffer;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<L16>(width);
        var rowSpan = row.GetSpan();
        using var plainMemory = allocator.Allocate<byte>(width * MaxCharsPerPixelGrayscaleWide);
        var plainSpan = plainMemory.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.ToL16(
                configuration,
                pixelSpan,
                rowSpan);

            var written = 0;
            for (var x = 0; x < width; x++)
            {
                Utf8Formatter.TryFormat(rowSpan[x].PackedValue, plainSpan.Slice(written), out var bytesWritten, DecimalFormat);
                written += bytesWritten;
                plainSpan[written++] = Space;
            }

            plainSpan[written - 1] = NewLine;
            stream.Write(plainSpan, 0, written);
        }
    }

    private static void WriteRgb<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var width = image.Width;
        var height = image.Height;
        var pixelBuffer = image.PixelBuffer;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<Rgb24>(width);
        var rowSpan = row.GetSpan();
        using var plainMemory = allocator.Allocate<byte>(width * MaxCharsPerPixelRgb);
        var plainSpan = plainMemory.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.ToRgb24(
                configuration,
                pixelSpan,
                rowSpan);

            var written = 0;
            for (var x = 0; x < width; x++)
            {
                Utf8Formatter.TryFormat(rowSpan[x].R, plainSpan.Slice(written), out var bytesWritten, DecimalFormat);
                written += bytesWritten;
                plainSpan[written++] = Space;
                Utf8Formatter.TryFormat(rowSpan[x].G, plainSpan.Slice(written), out bytesWritten, DecimalFormat);
                written += bytesWritten;
                plainSpan[written++] = Space;
                Utf8Formatter.TryFormat(rowSpan[x].B, plainSpan.Slice(written), out bytesWritten, DecimalFormat);
                written += bytesWritten;
                plainSpan[written++] = Space;
            }

            plainSpan[written - 1] = NewLine;
            stream.Write(plainSpan, 0, written);
        }
    }

    private static void WriteWideRgb<TPixel>(Configuration configuration, Stream stream, ImageFrame<TPixel> image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var width = image.Width;
        var height = image.Height;
        var pixelBuffer = image.PixelBuffer;
        var allocator = configuration.MemoryAllocator;
        using var row = allocator.Allocate<Rgb48>(width);
        var rowSpan = row.GetSpan();
        using var plainMemory = allocator.Allocate<byte>(width * MaxCharsPerPixelRgbWide);
        var plainSpan = plainMemory.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.ToRgb48(
                configuration,
                pixelSpan,
                rowSpan);

            var written = 0;
            for (var x = 0; x < width; x++)
            {
                Utf8Formatter.TryFormat(rowSpan[x].R, plainSpan.Slice(written), out var bytesWritten, DecimalFormat);
                written += bytesWritten;
                plainSpan[written++] = Space;
                Utf8Formatter.TryFormat(rowSpan[x].G, plainSpan.Slice(written), out bytesWritten, DecimalFormat);
                written += bytesWritten;
                plainSpan[written++] = Space;
                Utf8Formatter.TryFormat(rowSpan[x].B, plainSpan.Slice(written), out bytesWritten, DecimalFormat);
                written += bytesWritten;
                plainSpan[written++] = Space;
            }

            plainSpan[written - 1] = NewLine;
            stream.Write(plainSpan, 0, written);
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
        using var plainMemory = allocator.Allocate<byte>(width * MaxCharsPerPixelBlackAndWhite);
        var plainSpan = plainMemory.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var pixelSpan = pixelBuffer.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.ToL8(
                configuration,
                pixelSpan,
                rowSpan);

            var written = 0;
            for (var x = 0; x < width; x++)
            {
                var value = (rowSpan[x].PackedValue < 128) ? One : Zero;
                plainSpan[written++] = value;
                plainSpan[written++] = Space;
            }

            plainSpan[written - 1] = NewLine;
            stream.Write(plainSpan, 0, written);
        }
    }
}