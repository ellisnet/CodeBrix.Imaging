// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Processing.Processors.Convolution; //Was previously: namespace SixLabors.ImageSharp.Processing.Processors.Convolution;

/// <summary>
/// A <see langword="struct"/> implementing the logic for 2D convolution.
/// </summary>
internal readonly struct Convolution2DRowOperation<TPixel> : IRowOperation<Vector4>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Rectangle bounds;
    private readonly Buffer2D<TPixel> targetPixels;
    private readonly Buffer2D<TPixel> sourcePixels;
    private readonly KernelSamplingMap map;
    private readonly DenseMatrix<float> kernelMatrixY;
    private readonly DenseMatrix<float> kernelMatrixX;
    private readonly Configuration configuration;
    private readonly bool preserveAlpha;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Convolution2DRowOperation(
        Rectangle bounds,
        Buffer2D<TPixel> targetPixels,
        Buffer2D<TPixel> sourcePixels,
        KernelSamplingMap map,
        DenseMatrix<float> kernelMatrixY,
        DenseMatrix<float> kernelMatrixX,
        Configuration configuration,
        bool preserveAlpha)
    {
        this.bounds = bounds;
        this.targetPixels = targetPixels;
        this.sourcePixels = sourcePixels;
        this.map = map;
        this.kernelMatrixY = kernelMatrixY;
        this.kernelMatrixX = kernelMatrixX;
        this.configuration = configuration;
        this.preserveAlpha = preserveAlpha;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(int y, Span<Vector4> span)
    {
        if (this.preserveAlpha)
        {
            this.Convolve3(y, span);
        }
        else
        {
            this.Convolve4(y, span);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Convolve3(int y, Span<Vector4> span)
    {
        // Span is 3x bounds.
        var boundsX = this.bounds.X;
        var boundsWidth = this.bounds.Width;
        var sourceBuffer = span.Slice(0, boundsWidth);
        var targetYBuffer = span.Slice(boundsWidth, boundsWidth);
        var targetXBuffer = span.Slice(boundsWidth * 2, boundsWidth);

        var state = new Convolution2DState(in this.kernelMatrixY, in this.kernelMatrixX, this.map);
        ref var sampleRowBase = ref state.GetSampleRow(y - this.bounds.Y);

        // Clear the target buffers for each row run.
        targetYBuffer.Clear();
        targetXBuffer.Clear();
        ref var targetBaseY = ref MemoryMarshal.GetReference(targetYBuffer);
        ref var targetBaseX = ref MemoryMarshal.GetReference(targetXBuffer);

        var kernelY = state.KernelY;
        var kernelX = state.KernelX;
        Span<TPixel> sourceRow;
        for (var kY = 0; kY < kernelY.Rows; kY++)
        {
            // Get the precalculated source sample row for this kernel row and copy to our buffer.
            var sampleY = Unsafe.Add(ref sampleRowBase, kY);
            sourceRow = this.sourcePixels.DangerousGetRowSpan(sampleY).Slice(boundsX, boundsWidth);
            PixelOperations<TPixel>.Instance.ToVector4(this.configuration, sourceRow, sourceBuffer);

            ref var sourceBase = ref MemoryMarshal.GetReference(sourceBuffer);

            for (var x = 0; x < sourceBuffer.Length; x++)
            {
                ref var sampleColumnBase = ref state.GetSampleColumn(x);
                ref var targetY = ref Unsafe.Add(ref targetBaseY, x);
                ref var targetX = ref Unsafe.Add(ref targetBaseX, x);

                for (var kX = 0; kX < kernelY.Columns; kX++)
                {
                    var sampleX = Unsafe.Add(ref sampleColumnBase, kX) - boundsX;
                    var sample = Unsafe.Add(ref sourceBase, sampleX);
                    targetY += kernelX[kY, kX] * sample;
                    targetX += kernelY[kY, kX] * sample;
                }
            }
        }

        // Now we need to combine the values and copy the original alpha values
        // from the source row.
        sourceRow = this.sourcePixels.DangerousGetRowSpan(y).Slice(boundsX, boundsWidth);
        PixelOperations<TPixel>.Instance.ToVector4(this.configuration, sourceRow, sourceBuffer);

        for (var x = 0; x < sourceRow.Length; x++)
        {
            ref var target = ref Unsafe.Add(ref targetBaseY, x);
            var vectorY = target;
            var vectorX = Unsafe.Add(ref targetBaseX, x);

            target = Vector4.SquareRoot((vectorX * vectorX) + (vectorY * vectorY));
            target.W = Unsafe.Add(ref MemoryMarshal.GetReference(sourceBuffer), x).W;
        }

        var targetRowSpan = this.targetPixels.DangerousGetRowSpan(y).Slice(boundsX, boundsWidth);
        PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, targetYBuffer, targetRowSpan);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Convolve4(int y, Span<Vector4> span)
    {
        // Span is 3x bounds.
        var boundsX = this.bounds.X;
        var boundsWidth = this.bounds.Width;
        var sourceBuffer = span.Slice(0, boundsWidth);
        var targetYBuffer = span.Slice(boundsWidth, boundsWidth);
        var targetXBuffer = span.Slice(boundsWidth * 2, boundsWidth);

        var state = new Convolution2DState(in this.kernelMatrixY, in this.kernelMatrixX, this.map);
        ref var sampleRowBase = ref state.GetSampleRow(y - this.bounds.Y);

        // Clear the target buffers for each row run.
        targetYBuffer.Clear();
        targetXBuffer.Clear();
        ref var targetBaseY = ref MemoryMarshal.GetReference(targetYBuffer);
        ref var targetBaseX = ref MemoryMarshal.GetReference(targetXBuffer);

        var kernelY = state.KernelY;
        var kernelX = state.KernelX;
        for (var kY = 0; kY < kernelY.Rows; kY++)
        {
            // Get the precalculated source sample row for this kernel row and copy to our buffer.
            var sampleY = Unsafe.Add(ref sampleRowBase, kY);
            var sourceRow = this.sourcePixels.DangerousGetRowSpan(sampleY).Slice(boundsX, boundsWidth);
            PixelOperations<TPixel>.Instance.ToVector4(this.configuration, sourceRow, sourceBuffer);

            Numerics.Premultiply(sourceBuffer);
            ref var sourceBase = ref MemoryMarshal.GetReference(sourceBuffer);

            for (var x = 0; x < sourceBuffer.Length; x++)
            {
                ref var sampleColumnBase = ref state.GetSampleColumn(x);
                ref var targetY = ref Unsafe.Add(ref targetBaseY, x);
                ref var targetX = ref Unsafe.Add(ref targetBaseX, x);

                for (var kX = 0; kX < kernelY.Columns; kX++)
                {
                    var sampleX = Unsafe.Add(ref sampleColumnBase, kX) - boundsX;
                    var sample = Unsafe.Add(ref sourceBase, sampleX);
                    targetY += kernelX[kY, kX] * sample;
                    targetX += kernelY[kY, kX] * sample;
                }
            }
        }

        // Now we need to combine the values
        for (var x = 0; x < targetYBuffer.Length; x++)
        {
            ref var target = ref Unsafe.Add(ref targetBaseY, x);
            var vectorY = target;
            var vectorX = Unsafe.Add(ref targetBaseX, x);

            target = Vector4.SquareRoot((vectorX * vectorX) + (vectorY * vectorY));
        }

        Numerics.UnPremultiply(targetYBuffer);

        var targetRow = this.targetPixels.DangerousGetRowSpan(y).Slice(boundsX, boundsWidth);
        PixelOperations<TPixel>.Instance.FromVector4Destructive(this.configuration, targetYBuffer, targetRow);
    }
}