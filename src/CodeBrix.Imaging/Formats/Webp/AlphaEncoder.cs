// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Formats.Webp.Lossless;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Webp; //Was previously: namespace SixLabors.ImageSharp.Formats.Webp;

/// <summary>
/// Methods for encoding the alpha data of a VP8 image.
/// </summary>
internal class AlphaEncoder : IDisposable
{
    private IMemoryOwner<byte> alphaData;

    /// <summary>
    /// Encodes the alpha channel data.
    /// Data is either compressed as lossless webp image or uncompressed.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The <see cref="ImageFrame{TPixel}"/> to encode from.</param>
    /// <param name="configuration">The global configuration.</param>
    /// <param name="memoryAllocator">The memory manager.</param>
    /// <param name="compress">Indicates, if the data should be compressed with the lossless webp compression.</param>
    /// <param name="size">The size in bytes of the alpha data.</param>
    /// <returns>The encoded alpha data.</returns>
    public IMemoryOwner<byte> EncodeAlpha<TPixel>(Image<TPixel> image, Configuration configuration, MemoryAllocator memoryAllocator, bool compress, out int size)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var width = image.Width;
        var height = image.Height;
        this.alphaData = ExtractAlphaChannel(image, configuration, memoryAllocator);

        if (compress)
        {
            var effort = WebpEncodingMethod.Default;
            var quality = 8 * (int)effort;
            using var lossLessEncoder = new Vp8LEncoder(
                memoryAllocator,
                configuration,
                width,
                height,
                quality,
                effort,
                WebpTransparentColorMode.Preserve,
                false,
                0);

            // The transparency information will be stored in the green channel of the ARGB quadruplet.
            // The green channel is allowed extra transformation steps in the specification -- unlike the other channels,
            // that can improve compression.
            using var alphaAsImage = DispatchAlphaToGreen(image, this.alphaData.GetSpan());

            size = lossLessEncoder.EncodeAlphaImageData(alphaAsImage, this.alphaData);

            return this.alphaData;
        }

        size = width * height;
        return this.alphaData;
    }

    /// <summary>
    /// Store the transparency in the green channel.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The <see cref="ImageFrame{TPixel}"/> to encode from.</param>
    /// <param name="alphaData">A byte sequence of length width * height, containing all the 8-bit transparency values in scan order.</param>
    /// <returns>The transparency image.</returns>
    private static Image<Rgba32> DispatchAlphaToGreen<TPixel>(Image<TPixel> image, Span<byte> alphaData)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var width = image.Width;
        var height = image.Height;
        var alphaAsImage = new Image<Rgba32>(width, height, WebpFormat.Instance);

        for (var y = 0; y < height; y++)
        {
            var rowBuffer = alphaAsImage.DangerousGetPixelRowMemory(y);
            var pixelRow = rowBuffer.Span;
            var alphaRow = alphaData.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                // Leave A/R/B channels zero'd.
                pixelRow[x] = new Rgba32(0, alphaRow[x], 0, 0);
            }
        }

        return alphaAsImage;
    }

    /// <summary>
    /// Extract the alpha data of the image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The <see cref="ImageFrame{TPixel}"/> to encode from.</param>
    /// <param name="configuration">The global configuration.</param>
    /// <param name="memoryAllocator">The memory manager.</param>
    /// <returns>A byte sequence of length width * height, containing all the 8-bit transparency values in scan order.</returns>
    private static IMemoryOwner<byte> ExtractAlphaChannel<TPixel>(Image<TPixel> image, Configuration configuration, MemoryAllocator memoryAllocator)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var imageBuffer = image.Frames.RootFrame.PixelBuffer;
        var height = image.Height;
        var width = image.Width;
        var alphaDataBuffer = memoryAllocator.Allocate<byte>(width * height);
        var alphaData = alphaDataBuffer.GetSpan();

        using var rowBuffer = memoryAllocator.Allocate<Rgba32>(width);
        var rgbaRow = rowBuffer.GetSpan();

        for (var y = 0; y < height; y++)
        {
            var rowSpan = imageBuffer.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.ToRgba32(configuration, rowSpan, rgbaRow);
            var offset = y * width;
            for (var x = 0; x < width; x++)
            {
                alphaData[offset + x] = rgbaRow[x].A;
            }
        }

        return alphaDataBuffer;
    }

    /// <inheritdoc/>
    public void Dispose() => this.alphaData?.Dispose();
}