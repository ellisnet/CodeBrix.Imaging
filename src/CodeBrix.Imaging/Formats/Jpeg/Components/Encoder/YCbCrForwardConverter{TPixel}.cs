// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Jpeg.Components.Encoder; //Was previously: namespace SixLabors.ImageSharp.Formats.Jpeg.Components.Encoder;

internal static class YCbCrForwardConverter<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    public static void LoadAndStretchEdges(RowOctet<TPixel> source, Span<TPixel> dest, Point start, Size sampleSize, Size totalSize)
    {
        DebugGuard.MustBeBetweenOrEqualTo(start.X, 0, totalSize.Width - 1, nameof(start.X));
        DebugGuard.MustBeBetweenOrEqualTo(start.Y, 0, totalSize.Height - 1, nameof(start.Y));

        var width = Math.Min(sampleSize.Width, totalSize.Width - start.X);
        var height = Math.Min(sampleSize.Height, totalSize.Height - start.Y);

        var byteWidth = (uint)(width * Unsafe.SizeOf<TPixel>());
        var remainderXCount = sampleSize.Width - width;

        ref var blockStart = ref MemoryMarshal.GetReference(MemoryMarshal.Cast<TPixel, byte>(dest));
        var rowSizeInBytes = sampleSize.Width * Unsafe.SizeOf<TPixel>();

        for (var y = 0; y < height; y++)
        {
            var row = source[y];

            ref var s = ref Unsafe.As<TPixel, byte>(ref row[start.X]);
            ref var d = ref Unsafe.Add(ref blockStart, y * rowSizeInBytes);

            Unsafe.CopyBlock(ref d, ref s, byteWidth);

            ref var last = ref Unsafe.Add(ref Unsafe.As<byte, TPixel>(ref d), width - 1);

            for (var x = 1; x <= remainderXCount; x++)
            {
                Unsafe.Add(ref last, x) = last;
            }
        }

        var remainderYCount = sampleSize.Height - height;

        if (remainderYCount == 0)
        {
            return;
        }

        ref var lastRowStart = ref Unsafe.Add(ref blockStart, (height - 1) * rowSizeInBytes);

        for (var y = 1; y <= remainderYCount; y++)
        {
            ref var remStart = ref Unsafe.Add(ref lastRowStart, rowSizeInBytes * y);
            Unsafe.CopyBlock(ref remStart, ref lastRowStart, (uint)rowSizeInBytes);
        }
    }
}