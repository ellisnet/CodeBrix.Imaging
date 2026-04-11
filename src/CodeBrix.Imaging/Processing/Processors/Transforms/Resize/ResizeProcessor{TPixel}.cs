// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Processing.Processors.Transforms; //Was previously: namespace SixLabors.ImageSharp.Processing.Processors.Transforms;

/// <summary>
/// Implements resizing of images using various resamplers.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
internal class ResizeProcessor<TPixel> : TransformProcessor<TPixel>, IResamplingTransformImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly ResizeOptions options;
    private readonly int destinationWidth;
    private readonly int destinationHeight;
    private readonly IResampler resampler;
    private readonly Rectangle destinationRectangle;
    private Image<TPixel> destination;

    public ResizeProcessor(Configuration configuration, ResizeProcessor definition, Image<TPixel> source, Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
    {
        this.destinationWidth = definition.DestinationWidth;
        this.destinationHeight = definition.DestinationHeight;
        this.destinationRectangle = definition.DestinationRectangle;
        this.options = definition.Options;
        this.resampler = definition.Options.Sampler;
    }

    /// <inheritdoc/>
    protected override Size GetDestinationSize() => new(this.destinationWidth, this.destinationHeight);

    /// <inheritdoc/>
    protected override void BeforeImageApply(Image<TPixel> destination)
    {
        this.destination = destination;
        this.resampler.ApplyTransform(this);

        base.BeforeImageApply(destination);
    }

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source, ImageFrame<TPixel> destination)
    {
        // Everything happens in BeforeImageApply.
    }

    public void ApplyTransform<TResampler>(in TResampler sampler)
        where TResampler : struct, IResampler
    {
        var configuration = this.Configuration;
        var source = this.Source;
        var destination = this.destination;
        var sourceRectangle = this.SourceRectangle;
        var destinationRectangle = this.destinationRectangle;
        var compand = this.options.Compand;
        var premultiplyAlpha = this.options.PremultiplyAlpha;
        var fillColor = this.options.PadColor.ToPixel<TPixel>();
        var shouldFill = (this.options.Mode == ResizeMode.BoxPad || this.options.Mode == ResizeMode.Pad)
                         && this.options.PadColor != default;

        // Handle resize dimensions identical to the original
        if (source.Width == destination.Width
            && source.Height == destination.Height
            && sourceRectangle == destinationRectangle)
        {
            for (var i = 0; i < source.Frames.Count; i++)
            {
                var sourceFrame = source.Frames[i];
                var destinationFrame = destination.Frames[i];

                // The cloned will be blank here copy all the pixel data over
                sourceFrame.GetPixelMemoryGroup().CopyTo(destinationFrame.GetPixelMemoryGroup());
            }

            return;
        }

        var interest = Rectangle.Intersect(destinationRectangle, destination.Bounds());

        if (sampler is NearestNeighborResampler)
        {
            for (var i = 0; i < source.Frames.Count; i++)
            {
                var sourceFrame = source.Frames[i];
                var destinationFrame = destination.Frames[i];

                if (shouldFill)
                {
                    destinationFrame.Clear(fillColor);
                }

                ApplyNNResizeFrameTransform(
                    configuration,
                    sourceFrame,
                    destinationFrame,
                    sourceRectangle,
                    destinationRectangle,
                    interest);
            }

            return;
        }

        // Since all image frame dimensions have to be the same we can calculate
        // the kernel maps and reuse for all frames.
        var allocator = configuration.MemoryAllocator;
        using var horizontalKernelMap = ResizeKernelMap.Calculate(
            in sampler,
            destinationRectangle.Width,
            sourceRectangle.Width,
            allocator);

        using var verticalKernelMap = ResizeKernelMap.Calculate(
            in sampler,
            destinationRectangle.Height,
            sourceRectangle.Height,
            allocator);

        for (var i = 0; i < source.Frames.Count; i++)
        {
            var sourceFrame = source.Frames[i];
            var destinationFrame = destination.Frames[i];

            if (shouldFill)
            {
                destinationFrame.Clear(fillColor);
            }

            ApplyResizeFrameTransform(
                configuration,
                sourceFrame,
                destinationFrame,
                horizontalKernelMap,
                verticalKernelMap,
                sourceRectangle,
                destinationRectangle,
                interest,
                compand,
                premultiplyAlpha);
        }
    }

    private static void ApplyNNResizeFrameTransform(
        Configuration configuration,
        ImageFrame<TPixel> source,
        ImageFrame<TPixel> destination,
        Rectangle sourceRectangle,
        Rectangle destinationRectangle,
        Rectangle interest)
    {
        // Scaling factors
        var widthFactor = sourceRectangle.Width / (float)destinationRectangle.Width;
        var heightFactor = sourceRectangle.Height / (float)destinationRectangle.Height;

        var operation = new NNRowOperation(
            sourceRectangle,
            destinationRectangle,
            interest,
            widthFactor,
            heightFactor,
            source.PixelBuffer,
            destination.PixelBuffer);

        ParallelRowIterator.IterateRows(
            configuration,
            interest,
            in operation);
    }

    private static PixelConversionModifiers GetModifiers(bool compand, bool premultiplyAlpha)
    {
        if (premultiplyAlpha)
        {
            return PixelConversionModifiers.Premultiply.ApplyCompanding(compand);
        }
        else
        {
            return PixelConversionModifiers.None.ApplyCompanding(compand);
        }
    }

    private static void ApplyResizeFrameTransform(
        Configuration configuration,
        ImageFrame<TPixel> source,
        ImageFrame<TPixel> destination,
        ResizeKernelMap horizontalKernelMap,
        ResizeKernelMap verticalKernelMap,
        Rectangle sourceRectangle,
        Rectangle destinationRectangle,
        Rectangle interest,
        bool compand,
        bool premultiplyAlpha)
    {
        var alphaRepresentation = PixelOperations<TPixel>.Instance.GetPixelTypeInfo()?.AlphaRepresentation;

        // Premultiply only if alpha representation is unknown or Unassociated:
        var needsPremultiplication = alphaRepresentation == null || alphaRepresentation.Value == PixelAlphaRepresentation.Unassociated;
        premultiplyAlpha &= needsPremultiplication;
        var conversionModifiers = GetModifiers(compand, premultiplyAlpha);

        var sourceRegion = source.PixelBuffer.GetRegion(sourceRectangle);

        // To reintroduce parallel processing, we would launch multiple workers
        // for different row intervals of the image.
        using var worker = new ResizeWorker<TPixel>(
            configuration,
            sourceRegion,
            conversionModifiers,
            horizontalKernelMap,
            verticalKernelMap,
            interest,
            destinationRectangle.Location);
        worker.Initialize();

        var workingInterval = new RowInterval(interest.Top, interest.Bottom);
        worker.FillDestinationPixels(workingInterval, destination.PixelBuffer);
    }

    private readonly struct NNRowOperation : IRowOperation
    {
        private readonly Rectangle sourceBounds;
        private readonly Rectangle destinationBounds;
        private readonly Rectangle interest;
        private readonly float widthFactor;
        private readonly float heightFactor;
        private readonly Buffer2D<TPixel> source;
        private readonly Buffer2D<TPixel> destination;

        [MethodImpl(InliningOptions.ShortMethod)]
        public NNRowOperation(
            Rectangle sourceBounds,
            Rectangle destinationBounds,
            Rectangle interest,
            float widthFactor,
            float heightFactor,
            Buffer2D<TPixel> source,
            Buffer2D<TPixel> destination)
        {
            this.sourceBounds = sourceBounds;
            this.destinationBounds = destinationBounds;
            this.interest = interest;
            this.widthFactor = widthFactor;
            this.heightFactor = heightFactor;
            this.source = source;
            this.destination = destination;
        }

        [MethodImpl(InliningOptions.ShortMethod)]
        public void Invoke(int y)
        {
            var sourceX = this.sourceBounds.X;
            var sourceY = this.sourceBounds.Y;
            var destOriginX = this.destinationBounds.X;
            var destOriginY = this.destinationBounds.Y;
            var destLeft = this.interest.Left;
            var destRight = this.interest.Right;

            // Y coordinates of source points
            var sourceRow = this.source.DangerousGetRowSpan((int)(((y - destOriginY) * this.heightFactor) + sourceY));
            var targetRow = this.destination.DangerousGetRowSpan(y);

            for (var x = destLeft; x < destRight; x++)
            {
                // X coordinates of source points
                targetRow[x] = sourceRow[(int)(((x - destOriginX) * this.widthFactor) + sourceX)];
            }
        }
    }
}