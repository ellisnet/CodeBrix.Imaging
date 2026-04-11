// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Processing; //Was previously: namespace SixLabors.ImageSharp.Processing;

/// <summary>
/// Defines extensions that allow the computation of image integrals on an <see cref="Image"/>
/// </summary>
public static partial class ProcessingExtensions
{
    /// <summary>
    /// Apply an image integral. <See href="https://en.wikipedia.org/wiki/Summed-area_table"/>
    /// </summary>
    /// <param name="source">The image on which to apply the integral.</param>
    /// <typeparam name="TPixel">The type of the pixel.</typeparam>
    /// <returns>The <see cref="Buffer2D{T}"/> containing all the sums.</returns>
    public static Buffer2D<ulong> CalculateIntegralImage<TPixel>(this Image<TPixel> source)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var configuration = source.GetConfiguration();

        var endY = source.Height;
        var endX = source.Width;

        var intImage = configuration.MemoryAllocator.Allocate2D<ulong>(source.Width, source.Height);
        ulong sumX0 = 0;
        var sourceBuffer = source.Frames.RootFrame.PixelBuffer;

        using (var tempRow = configuration.MemoryAllocator.Allocate<L8>(source.Width))
        {
            var tempSpan = tempRow.GetSpan();
            var sourceRow = sourceBuffer.DangerousGetRowSpan(0);
            var destRow = intImage.DangerousGetRowSpan(0);

            PixelOperations<TPixel>.Instance.ToL8(configuration, sourceRow, tempSpan);

            // First row
            for (var x = 0; x < endX; x++)
            {
                sumX0 += tempSpan[x].PackedValue;
                destRow[x] = sumX0;
            }

            var previousDestRow = destRow;

            // All other rows
            for (var y = 1; y < endY; y++)
            {
                sourceRow = sourceBuffer.DangerousGetRowSpan(y);
                destRow = intImage.DangerousGetRowSpan(y);

                PixelOperations<TPixel>.Instance.ToL8(configuration, sourceRow, tempSpan);

                // Process first column
                sumX0 = tempSpan[0].PackedValue;
                destRow[0] = sumX0 + previousDestRow[0];

                // Process all other colmns
                for (var x = 1; x < endX; x++)
                {
                    sumX0 += tempSpan[x].PackedValue;
                    destRow[x] = sumX0 + previousDestRow[x];
                }

                previousDestRow = destRow;
            }
        }

        return intImage;
    }
}