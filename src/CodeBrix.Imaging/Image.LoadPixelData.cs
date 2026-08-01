// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.InteropServices;
using CodeBrix.Imaging.Formats;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;
using CodeBrix.Imaging.PixelFormats.Utils;

namespace CodeBrix.Imaging; //Was previously: namespace SixLabors.ImageSharp;

/// <content>
/// Adds static methods allowing the creation of new image from raw pixel data.
/// </content>
public abstract partial class Image
{
    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from the raw <typeparamref name="TPixel"/> data.
    /// </summary>
    /// <param name="data">The byte array containing image data.</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/>.</returns>
    public static Image<TPixel> LoadPixelData<TPixel>(
        TPixel[] data, 
        int width, 
        int height,
        IImageFormat expectedFormat)
        where TPixel : unmanaged, IPixel<TPixel>
        => LoadPixelData(Configuration.Default, data, width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from the raw <typeparamref name="TPixel"/> data.
    /// </summary>
    /// <param name="data">The byte array containing image data.</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/>.</returns>
    public static Image<TPixel> LoadPixelData<TPixel>(
        ReadOnlySpan<TPixel> data, 
        int width, 
        int height,
        IImageFormat expectedFormat)
        where TPixel : unmanaged, IPixel<TPixel>
        => LoadPixelData(Configuration.Default, data, width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from the given byte array in <typeparamref name="TPixel"/> format.
    /// </summary>
    /// <param name="data">The byte array containing image data.</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/>.</returns>
    public static Image<TPixel> LoadPixelData<TPixel>(
        byte[] data, 
        int width, 
        int height,
        IImageFormat expectedFormat)
        where TPixel : unmanaged, IPixel<TPixel>
        => LoadPixelData<TPixel>(Configuration.Default, data, width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from the given byte array in <typeparamref name="TPixel"/> format.
    /// </summary>
    /// <param name="data">The byte array containing image data.</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/>.</returns>
    public static Image<TPixel> LoadPixelData<TPixel>(
        ReadOnlySpan<byte> data, 
        int width, 
        int height,
        IImageFormat expectedFormat)
        where TPixel : unmanaged, IPixel<TPixel>
        => LoadPixelData<TPixel>(Configuration.Default, data, width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from the given byte array in <typeparamref name="TPixel"/> format.
    /// </summary>
    /// <param name="configuration">The configuration for the decoder.</param>
    /// <param name="data">The byte array containing image data.</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/>.</returns>
    public static Image<TPixel> LoadPixelData<TPixel>(
        Configuration configuration, 
        byte[] data, 
        int width, 
        int height,
        IImageFormat expectedFormat)
        where TPixel : unmanaged, IPixel<TPixel>
        => LoadPixelData(configuration, MemoryMarshal.Cast<byte, TPixel>(new ReadOnlySpan<byte>(data)), width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from the given byte array in <typeparamref name="TPixel"/> format.
    /// </summary>
    /// <param name="configuration">The configuration for the decoder.</param>
    /// <param name="data">The byte array containing image data.</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/>.</returns>
    public static Image<TPixel> LoadPixelData<TPixel>(
        Configuration configuration, 
        ReadOnlySpan<byte> data, 
        int width, 
        int height,
        IImageFormat expectedFormat)
        where TPixel : unmanaged, IPixel<TPixel>
        => LoadPixelData(configuration, MemoryMarshal.Cast<byte, TPixel>(data), width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from the raw <typeparamref name="TPixel"/> data.
    /// </summary>
    /// <param name="configuration">The configuration for the decoder.</param>
    /// <param name="data">The Span containing the image Pixel data.</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/>.</returns>
    public static Image<TPixel> LoadPixelData<TPixel>(
        Configuration configuration, 
        TPixel[] data, 
        int width, 
        int height,
        IImageFormat expectedFormat)
        where TPixel : unmanaged, IPixel<TPixel>
        => LoadPixelData(configuration, new ReadOnlySpan<TPixel>(data), width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from the raw <typeparamref name="TPixel"/> data.
    /// </summary>
    /// <param name="configuration">The configuration for the decoder.</param>
    /// <param name="data">The Span containing the image Pixel data.</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <returns>A new <see cref="Image{TPixel}"/>.</returns>
    public static Image<TPixel> LoadPixelData<TPixel>(
        Configuration configuration, 
        ReadOnlySpan<TPixel> data, 
        int width, 
        int height,
        IImageFormat expectedFormat)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));

        var longCount = (long)width * height;
        Guard.MustBeLessThanOrEqualTo(longCount, (long)int.MaxValue, nameof(data));

        var count = (int)longCount;
        Guard.MustBeGreaterThanOrEqualTo(data.Length, count, nameof(data));

        var image = new Image<TPixel>(configuration, width, height, expectedFormat);
        data = data.Slice(0, count);
        data.CopyTo(image.Frames.RootFrame.PixelBuffer.FastMemoryGroup);

        return image;
    }

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from BGRA-ordered byte data,
    /// converting to <see cref="Rgba32"/> pixel format using SIMD-optimized channel reordering.
    /// This is significantly faster than manually swapping bytes before calling
    /// <see cref="LoadPixelData{TPixel}(byte[], int, int, IImageFormat)"/>.
    /// </summary>
    /// <param name="data">The byte array containing BGRA pixel data (4 bytes per pixel: B, G, R, A).</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/> with pixels converted to RGBA order.</returns>
    public static Image<Rgba32> LoadPixelDataFromBgra(
        byte[] data,
        int width,
        int height,
        IImageFormat expectedFormat)
        => LoadPixelDataFromBgra(Configuration.Default, new ReadOnlySpan<byte>(data), width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from BGRA-ordered byte data,
    /// converting to <see cref="Rgba32"/> pixel format using SIMD-optimized channel reordering.
    /// This is significantly faster than manually swapping bytes before calling
    /// <see cref="LoadPixelData{TPixel}(ReadOnlySpan{byte}, int, int, IImageFormat)"/>.
    /// </summary>
    /// <param name="data">The span containing BGRA pixel data (4 bytes per pixel: B, G, R, A).</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/> with pixels converted to RGBA order.</returns>
    public static Image<Rgba32> LoadPixelDataFromBgra(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        IImageFormat expectedFormat)
        => LoadPixelDataFromBgra(Configuration.Default, data, width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from BGRA-ordered byte data,
    /// converting to <see cref="Rgba32"/> pixel format using SIMD-optimized channel reordering.
    /// This is significantly faster than manually swapping bytes before calling
    /// <see cref="LoadPixelData{TPixel}(Configuration, byte[], int, int, IImageFormat)"/>.
    /// </summary>
    /// <param name="configuration">The configuration for the decoder.</param>
    /// <param name="data">The byte array containing BGRA pixel data (4 bytes per pixel: B, G, R, A).</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/> with pixels converted to RGBA order.</returns>
    public static Image<Rgba32> LoadPixelDataFromBgra(
        Configuration configuration,
        byte[] data,
        int width,
        int height,
        IImageFormat expectedFormat)
        => LoadPixelDataFromBgra(configuration, new ReadOnlySpan<byte>(data), width, height, expectedFormat);

    /// <summary>
    /// Create a new instance of the <see cref="Image{TPixel}"/> class from BGRA-ordered byte data,
    /// converting to <see cref="Rgba32"/> pixel format using SIMD-optimized channel reordering.
    /// This is significantly faster than manually swapping bytes before calling
    /// <see cref="LoadPixelData{TPixel}(Configuration, ReadOnlySpan{byte}, int, int, IImageFormat)"/>.
    /// </summary>
    /// <param name="configuration">The configuration for the decoder.</param>
    /// <param name="data">The span containing BGRA pixel data (4 bytes per pixel: B, G, R, A).</param>
    /// <param name="width">The width of the final image.</param>
    /// <param name="height">The height of the final image.</param>
    /// <param name="expectedFormat">The expected format of the final image.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="ArgumentException">The data length is incorrect.</exception>
    /// <returns>A new <see cref="Image{TPixel}"/> with pixels converted to RGBA order.</returns>
    public static Image<Rgba32> LoadPixelDataFromBgra(
        Configuration configuration,
        ReadOnlySpan<byte> data,
        int width,
        int height,
        IImageFormat expectedFormat)
    {
        Guard.NotNull(configuration, nameof(configuration));

        // The binding constraint is the BYTE count, not the pixel count: BGRA data uses
        // 4 bytes per pixel, so a pixel count that fits in an Int32 can still produce a
        // byte count that does not. Both are computed as Int64 and the byte count is the
        // value that gets range-checked; computing it as Int32 would silently wrap
        // negative for images larger than 536,870,911 pixels, which made the length guard
        // below pass vacuously and produced a meaningless slicing exception further down.
        var longCount = (long)width * height;
        var longByteCount = longCount * 4;
        Guard.MustBeLessThanOrEqualTo(longByteCount, (long)int.MaxValue, nameof(data));

        var byteCount = (int)longByteCount;
        Guard.MustBeGreaterThanOrEqualTo(data.Length, byteCount, nameof(data));

        var image = new Image<Rgba32>(configuration, width, height, expectedFormat);

        try
        {
            var source = data.Slice(0, byteCount);

            // Convert BGRA to RGBA using the SIMD-optimized PixelConverter, writing
            // directly into the image's pixel buffer segments. This handles potentially
            // discontiguous memory groups while using hardware-accelerated (AVX2/SSSE3)
            // channel reordering for maximum throughput.
            IMemoryGroup<Rgba32> memoryGroup = image.Frames.RootFrame.PixelBuffer.FastMemoryGroup;
            foreach (var memory in memoryGroup)
            {
                // Safe to compute as Int32: the segment lengths sum to the total pixel
                // count, so segmentBytes can never exceed the guarded byteCount.
                var rgbaSpan = memory.Span;
                var segmentBytes = rgbaSpan.Length * 4;
                var destBytes = MemoryMarshal.Cast<Rgba32, byte>(rgbaSpan);

                PixelConverter.FromBgra32.ToRgba32(source.Slice(0, segmentBytes), destBytes);
                source = source.Slice(segmentBytes);
            }
        }
        catch
        {
            // Do not leak the image (and the pooled unmanaged memory backing it) if the
            // pixel conversion fails part way through.
            image.Dispose();
            throw;
        }

        return image;
    }
}