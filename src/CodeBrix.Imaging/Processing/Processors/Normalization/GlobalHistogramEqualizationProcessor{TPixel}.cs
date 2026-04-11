// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Processing.Processors.Normalization; //Was previously: namespace SixLabors.ImageSharp.Processing.Processors.Normalization;

/// <summary>
/// Applies a global histogram equalization to the image.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal class GlobalHistogramEqualizationProcessor<TPixel> : HistogramEqualizationProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalHistogramEqualizationProcessor{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration which allows altering default behaviour or extending the library.</param>
    /// <param name="luminanceLevels">
    /// The number of different luminance levels. Typical values are 256 for 8-bit grayscale images
    /// or 65536 for 16-bit grayscale images.
    /// </param>
    /// <param name="clipHistogram">Indicating whether to clip the histogram bins at a specific value.</param>
    /// <param name="clipLimit">The histogram clip limit. Histogram bins which exceed this limit, will be capped at this value.</param>
    /// <param name="source">The source <see cref="Image{TPixel}"/> for the current processor instance.</param>
    /// <param name="sourceRectangle">The source area to process for the current processor instance.</param>
    public GlobalHistogramEqualizationProcessor(
        Configuration configuration,
        int luminanceLevels,
        bool clipHistogram,
        int clipLimit,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        : base(configuration, luminanceLevels, clipHistogram, clipLimit, source, sourceRectangle)
    {
    }

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        var memoryAllocator = this.Configuration.MemoryAllocator;
        var numberOfPixels = source.Width * source.Height;
        var interest = Rectangle.Intersect(this.SourceRectangle, source.Bounds());

        using var histogramBuffer = memoryAllocator.Allocate<int>(this.LuminanceLevels, AllocationOptions.Clean);

        // Build the histogram of the grayscale levels.
        var grayscaleOperation = new GrayscaleLevelsRowOperation(interest, histogramBuffer, source.PixelBuffer, this.LuminanceLevels);
        ParallelRowIterator.IterateRows(
            this.Configuration,
            interest,
            in grayscaleOperation);

        var histogram = histogramBuffer.GetSpan();
        if (this.ClipHistogramEnabled)
        {
            this.ClipHistogram(histogram, this.ClipLimit);
        }

        using var cdfBuffer = memoryAllocator.Allocate<int>(this.LuminanceLevels, AllocationOptions.Clean);

        // Calculate the cumulative distribution function, which will map each input pixel to a new value.
        var cdfMin = this.CalculateCdf(
            ref MemoryMarshal.GetReference(cdfBuffer.GetSpan()),
            ref MemoryMarshal.GetReference(histogram),
            histogram.Length - 1);

        float numberOfPixelsMinusCdfMin = numberOfPixels - cdfMin;

        // Apply the cdf to each pixel of the image
        var cdfOperation = new CdfApplicationRowOperation(interest, cdfBuffer, source.PixelBuffer, this.LuminanceLevels, numberOfPixelsMinusCdfMin);
        ParallelRowIterator.IterateRows(
            this.Configuration,
            interest,
            in cdfOperation);
    }

    /// <summary>
    /// A <see langword="struct"/> implementing the grayscale levels logic for <see cref="GlobalHistogramEqualizationProcessor{TPixel}"/>.
    /// </summary>
    private readonly struct GrayscaleLevelsRowOperation : IRowOperation
    {
        private readonly Rectangle bounds;
        private readonly IMemoryOwner<int> histogramBuffer;
        private readonly Buffer2D<TPixel> source;
        private readonly int luminanceLevels;

        [MethodImpl(InliningOptions.ShortMethod)]
        public GrayscaleLevelsRowOperation(
            Rectangle bounds,
            IMemoryOwner<int> histogramBuffer,
            Buffer2D<TPixel> source,
            int luminanceLevels)
        {
            this.bounds = bounds;
            this.histogramBuffer = histogramBuffer;
            this.source = source;
            this.luminanceLevels = luminanceLevels;
        }

        /// <inheritdoc/>
        [MethodImpl(InliningOptions.ShortMethod)]
        public void Invoke(int y)
        {
            ref var histogramBase = ref MemoryMarshal.GetReference(this.histogramBuffer.GetSpan());
            var pixelRow = this.source.DangerousGetRowSpan(y);
            var levels = this.luminanceLevels;

            for (var x = 0; x < this.bounds.Width; x++)
            {
                // TODO: We should bulk convert here.
                var vector = pixelRow[x].ToVector4();
                var luminance = ColorNumerics.GetBT709Luminance(ref vector, levels);
                Interlocked.Increment(ref Unsafe.Add(ref histogramBase, luminance));
            }
        }
    }

    /// <summary>
    /// A <see langword="struct"/> implementing the cdf application levels logic for <see cref="GlobalHistogramEqualizationProcessor{TPixel}"/>.
    /// </summary>
    private readonly struct CdfApplicationRowOperation : IRowOperation
    {
        private readonly Rectangle bounds;
        private readonly IMemoryOwner<int> cdfBuffer;
        private readonly Buffer2D<TPixel> source;
        private readonly int luminanceLevels;
        private readonly float numberOfPixelsMinusCdfMin;

        [MethodImpl(InliningOptions.ShortMethod)]
        public CdfApplicationRowOperation(
            Rectangle bounds,
            IMemoryOwner<int> cdfBuffer,
            Buffer2D<TPixel> source,
            int luminanceLevels,
            float numberOfPixelsMinusCdfMin)
        {
            this.bounds = bounds;
            this.cdfBuffer = cdfBuffer;
            this.source = source;
            this.luminanceLevels = luminanceLevels;
            this.numberOfPixelsMinusCdfMin = numberOfPixelsMinusCdfMin;
        }

        /// <inheritdoc/>
        [MethodImpl(InliningOptions.ShortMethod)]
        public void Invoke(int y)
        {
            ref var cdfBase = ref MemoryMarshal.GetReference(this.cdfBuffer.GetSpan());
            var pixelRow = this.source.DangerousGetRowSpan(y);
            var levels = this.luminanceLevels;
            var noOfPixelsMinusCdfMin = this.numberOfPixelsMinusCdfMin;

            for (var x = 0; x < this.bounds.Width; x++)
            {
                // TODO: We should bulk convert here.
                ref var pixel = ref pixelRow[x];
                var vector = pixel.ToVector4();
                var luminance = ColorNumerics.GetBT709Luminance(ref vector, levels);
                var luminanceEqualized = Unsafe.Add(ref cdfBase, luminance) / noOfPixelsMinusCdfMin;
                pixel.FromVector4(new Vector4(luminanceEqualized, luminanceEqualized, luminanceEqualized, vector.W));
            }
        }
    }
}