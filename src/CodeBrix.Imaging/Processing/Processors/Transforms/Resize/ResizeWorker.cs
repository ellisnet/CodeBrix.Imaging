// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Processing.Processors.Transforms; //Was previously: namespace SixLabors.ImageSharp.Processing.Processors.Transforms;

/// <summary>
/// Implements the resize algorithm using a sliding window of size
/// maximized by <see cref="Configuration.WorkingBufferSizeHintInBytes"/>.
/// The height of the window is a multiple of the vertical kernel's maximum diameter.
/// When sliding the window, the contents of the bottom window band are copied to the new top band.
/// For more details, and visual explanation, see "ResizeWorker.pptx".
/// </summary>
internal sealed class ResizeWorker<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Buffer2D<Vector4> transposedFirstPassBuffer;

    private readonly Configuration configuration;

    private readonly PixelConversionModifiers conversionModifiers;

    private readonly ResizeKernelMap horizontalKernelMap;

    private readonly Buffer2DRegion<TPixel> source;

    private readonly Rectangle sourceRectangle;

    private readonly IMemoryOwner<Vector4> tempRowBuffer;

    private readonly IMemoryOwner<Vector4> tempColumnBuffer;

    private readonly ResizeKernelMap verticalKernelMap;

    private readonly Rectangle targetWorkingRect;

    private readonly Point targetOrigin;

    private readonly int windowBandHeight;

    private readonly int workerHeight;

    private RowInterval currentWindow;

    public ResizeWorker(
        Configuration configuration,
        Buffer2DRegion<TPixel> source,
        PixelConversionModifiers conversionModifiers,
        ResizeKernelMap horizontalKernelMap,
        ResizeKernelMap verticalKernelMap,
        Rectangle targetWorkingRect,
        Point targetOrigin)
    {
        this.configuration = configuration;
        this.source = source;
        this.sourceRectangle = source.Rectangle;
        this.conversionModifiers = conversionModifiers;
        this.horizontalKernelMap = horizontalKernelMap;
        this.verticalKernelMap = verticalKernelMap;
        this.targetWorkingRect = targetWorkingRect;
        this.targetOrigin = targetOrigin;

        this.windowBandHeight = verticalKernelMap.MaxDiameter;

        // We need to make sure the working buffer is contiguous:
        var workingBufferLimitHintInBytes = Math.Min(
            configuration.WorkingBufferSizeHintInBytes,
            configuration.MemoryAllocator.GetBufferCapacityInBytes());

        var numberOfWindowBands = ResizeHelper.CalculateResizeWorkerHeightInWindowBands(
            this.windowBandHeight,
            targetWorkingRect.Width,
            workingBufferLimitHintInBytes);

        this.workerHeight = Math.Min(this.sourceRectangle.Height, numberOfWindowBands * this.windowBandHeight);

        this.transposedFirstPassBuffer = configuration.MemoryAllocator.Allocate2D<Vector4>(
            this.workerHeight,
            targetWorkingRect.Width,
            preferContiguosImageBuffers: true,
            options: AllocationOptions.Clean);

        this.tempRowBuffer = configuration.MemoryAllocator.Allocate<Vector4>(this.sourceRectangle.Width);
        this.tempColumnBuffer = configuration.MemoryAllocator.Allocate<Vector4>(targetWorkingRect.Width);

        this.currentWindow = new RowInterval(0, this.workerHeight);
    }

    public void Dispose()
    {
        this.transposedFirstPassBuffer.Dispose();
        this.tempRowBuffer.Dispose();
        this.tempColumnBuffer.Dispose();
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    public Span<Vector4> GetColumnSpan(int x, int startY)
        => this.transposedFirstPassBuffer.DangerousGetRowSpan(x).Slice(startY - this.currentWindow.Min);

    public void Initialize()
        => this.CalculateFirstPassValues(this.currentWindow);

    public void FillDestinationPixels(RowInterval rowInterval, Buffer2D<TPixel> destination)
    {
        var tempColSpan = this.tempColumnBuffer.GetSpan();

        // When creating transposedFirstPassBuffer, we made sure it's contiguous:
        var transposedFirstPassBufferSpan = this.transposedFirstPassBuffer.DangerousGetSingleSpan();

        var left = this.targetWorkingRect.Left;
        var right = this.targetWorkingRect.Right;
        var width = this.targetWorkingRect.Width;
        for (var y = rowInterval.Min; y < rowInterval.Max; y++)
        {
            // Ensure offsets are normalized for cropping and padding.
            var kernel = this.verticalKernelMap.GetKernel(y - this.targetOrigin.Y);

            while (kernel.StartIndex + kernel.Length > this.currentWindow.Max)
            {
                this.Slide();
            }

            ref var tempRowBase = ref MemoryMarshal.GetReference(tempColSpan);

            var top = kernel.StartIndex - this.currentWindow.Min;

            ref var fpBase = ref transposedFirstPassBufferSpan[top];

            for (nint x = 0; x < (right - left); x++)
            {
                ref var firstPassColumnBase = ref Unsafe.Add(ref fpBase, x * this.workerHeight);

                // Destination color components
                Unsafe.Add(ref tempRowBase, x) = kernel.ConvolveCore(ref firstPassColumnBase);
            }

            var targetRowSpan = destination.DangerousGetRowSpan(y).Slice(left, width);

            PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, tempColSpan, targetRowSpan, this.conversionModifiers);
        }
    }

    private void Slide()
    {
        var minY = this.currentWindow.Max - this.windowBandHeight;
        var maxY = Math.Min(minY + this.workerHeight, this.sourceRectangle.Height);

        // Copy previous bottom band to the new top:
        // (rows <--> columns, because the buffer is transposed)
        this.transposedFirstPassBuffer.DangerousCopyColumns(
            this.workerHeight - this.windowBandHeight,
            0,
            this.windowBandHeight);

        this.currentWindow = new RowInterval(minY, maxY);

        // Calculate the remainder:
        this.CalculateFirstPassValues(this.currentWindow.Slice(this.windowBandHeight));
    }

    private void CalculateFirstPassValues(RowInterval calculationInterval)
    {
        var tempRowSpan = this.tempRowBuffer.GetSpan();
        var transposedFirstPassBufferSpan = this.transposedFirstPassBuffer.DangerousGetSingleSpan();

        var left = this.targetWorkingRect.Left;
        var right = this.targetWorkingRect.Right;
        var targetOriginX = this.targetOrigin.X;
        for (var y = calculationInterval.Min; y < calculationInterval.Max; y++)
        {
            var sourceRow = this.source.DangerousGetRowSpan(y);

            PixelOperations<TPixel>.Instance.ToVector4(
                this.configuration,
                sourceRow,
                tempRowSpan,
                this.conversionModifiers);

            // optimization for:
            // Span<Vector4> firstPassSpan = transposedFirstPassBufferSpan.Slice(y - this.currentWindow.Min);
            ref var firstPassBaseRef = ref transposedFirstPassBufferSpan[y - this.currentWindow.Min];

            for (nint x = left, z = 0; x < right; x++, z++)
            {
                var kernel = this.horizontalKernelMap.GetKernel(x - targetOriginX);

                // optimization for:
                // firstPassSpan[x * this.workerHeight] = kernel.Convolve(tempRowSpan);
                Unsafe.Add(ref firstPassBaseRef, z * this.workerHeight) = kernel.Convolve(tempRowSpan);
            }
        }
    }
}