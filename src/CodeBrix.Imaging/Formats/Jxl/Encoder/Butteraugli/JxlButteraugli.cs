// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/butteraugli/butteraugli.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using static CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli.JxlButteraugliKernels;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;

// Inputs are finite, linear-light sRGB planes. Original psycho-images are
// retained across comparisons so quantization searches can reuse their work.
internal sealed class JxlButteraugli : IDisposable
{
    private readonly int width, height;
    private readonly float intensity, asymmetry, xMultiplier;
    private readonly MemoryAllocator allocator;
    private readonly JxlFloatFrame original, halfOriginal;
    private bool disposed;

    internal JxlButteraugli(JxlFloatFrame reference, float intensity, CancellationToken cancellationToken,
        float asymmetry = 1, float xMultiplier = 1)
    {
        Validate(reference, cancellationToken);
        if (!float.IsFinite(intensity) || intensity <= 0) { throw new ArgumentOutOfRangeException(nameof(intensity)); }
        if (!float.IsFinite(asymmetry) || asymmetry <= 0) { throw new ArgumentOutOfRangeException(nameof(asymmetry)); }
        if (!float.IsFinite(xMultiplier) || xMultiplier <= 0) { throw new ArgumentOutOfRangeException(nameof(xMultiplier)); }
        this.width = reference.Planes[0].Width; this.height = reference.Planes[0].Height;
        this.allocator = reference.Allocator; this.intensity = intensity; this.asymmetry = asymmetry; this.xMultiplier = xMultiplier;
        try
        {
            using JxlFloatFrame padded = this.Pad(reference, cancellationToken);
            JxlFloatFrame input = padded ?? reference;
            this.original = PsychoImage(input, intensity, cancellationToken);
            if (input.Planes[0].Width >= 15 && input.Planes[0].Height >= 15)
            {
                using JxlFloatFrame half = Subsample(input, cancellationToken);
                this.halfOriginal = PsychoImage(half, intensity, cancellationToken);
            }
        }
        catch { this.Dispose(); throw; }
    }

    internal float Compare(JxlFloatFrame distorted, Buffer2D<float> difference, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        Validate(distorted, cancellationToken);
        if (distorted.Planes[0].Width != this.width || distorted.Planes[0].Height != this.height ||
            difference.Width != this.width || difference.Height != this.height)
        { throw new ArgumentException("Butteraugli image dimensions must match."); }
        using JxlFloatFrame padded = this.Pad(distorted, cancellationToken);
        JxlFloatFrame input = padded ?? distorted;
        using JxlFloatFrame psycho = PsychoImage(input, this.intensity, cancellationToken);
        using Buffer2D<float> paddedResult = padded == null ? null : this.allocator.Allocate2D<float>(input.Planes[0].Width, input.Planes[0].Height);
        Buffer2D<float> result = paddedResult ?? difference;
        JxlButteraugliMask.Compare(this.original, psycho, result, this.asymmetry, this.xMultiplier, cancellationToken);
        if (this.halfOriginal != null)
        {
            using JxlFloatFrame half = Subsample(input, cancellationToken);
            using JxlFloatFrame halfPsycho = PsychoImage(half, this.intensity, cancellationToken);
            using Buffer2D<float> halfDifference = this.allocator.Allocate2D<float>(half.Planes[0].Width, half.Planes[0].Height);
            JxlButteraugliMask.Compare(this.halfOriginal, halfPsycho, halfDifference, this.asymmetry, this.xMultiplier, cancellationToken);
            for (int y = 0; y < result.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> row = result.DangerousGetRowSpan(y), small = halfDifference.DangerousGetRowSpan(y / 2);
                for (int x = 0; x < result.Width; x++) { row[x] = (float)(row[x] * .85) + .5f * small[x / 2]; }
            }
        }
        int left = (result.Width - this.width) / 2, top = (result.Height - this.height) / 2;
        float maximum = 0;
        for (int y = 0; y < this.height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> row = result.DangerousGetRowSpan(y + top).Slice(left, this.width);
            if (paddedResult != null) { row.CopyTo(difference.DangerousGetRowSpan(y)); }
            foreach (float value in row) { maximum = Math.Max(maximum, value); }
        }
        return maximum;
    }

    public void Dispose()
    {
        if (this.disposed) { return; }
        this.disposed = true; this.original?.Dispose(); this.halfOriginal?.Dispose();
    }

    private JxlFloatFrame Pad(JxlFloatFrame input, CancellationToken cancellationToken)
    {
        if (this.width >= 8 && this.height >= 8) { return null; }
        int w = Math.Max(8, this.width), h = Math.Max(8, this.height), left = (w - this.width) / 2, top = (h - this.height) / 2;
        JxlFloatFrame result = Create(w, h, 3, this.allocator);
        try
        {
            for (int c = 0; c < 3; c++)
            {
                for (int y = 0; y < h; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Span<float> source = input.Planes[c].DangerousGetRowSpan(Math.Clamp(y - top, 0, this.height - 1)), row = result.Planes[c].DangerousGetRowSpan(y);
                    for (int x = 0; x < w; x++) { row[x] = source[Math.Clamp(x - left, 0, this.width - 1)]; }
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static JxlFloatFrame Subsample(JxlFloatFrame input, CancellationToken cancellationToken)
    {
        int width = input.Planes[0].Width, height = input.Planes[0].Height;
        JxlFloatFrame result = Create((width + 1) / 2, (height + 1) / 2, 3, input.Allocator);
        try
        {
            for (int c = 0; c < 3; c++)
            {
                Buffer2D<float> destination = result.Planes[c];
                for (int y = 0; y < destination.Height; y++) { destination.DangerousGetRowSpan(y).Clear(); }
                for (int y = 0; y < height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Span<float> source = input.Planes[c].DangerousGetRowSpan(y), row = destination.DangerousGetRowSpan(y / 2);
                    for (int x = 0; x < width; x++) { row[x / 2] += .25f * source[x]; }
                }
                if ((width & 1) != 0)
                { for (int y = 0; y < destination.Height; y++) { destination.DangerousGetRowSpan(y)[destination.Width - 1] *= 2; } }
                if ((height & 1) != 0)
                { Span<float> row = destination.DangerousGetRowSpan(destination.Height - 1); for (int x = 0; x < row.Length; x++) { row[x] *= 2; } }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static void Validate(JxlFloatFrame input, CancellationToken cancellationToken)
    {
        if (input == null || input.ColorChannels != 3 || input.Planes.Length < 3 || input.Planes[0] == null)
        { throw new ArgumentException("Butteraugli needs three linear sRGB planes.", nameof(input)); }
        int width = input.Planes[0].Width, height = input.Planes[0].Height;
        if (width < 1 || height < 1) { throw new ArgumentException("Butteraugli needs a nonempty image.", nameof(input)); }
        for (int c = 0; c < 3; c++)
        {
            if (input.Planes[c] == null || input.Planes[c].Width != width || input.Planes[c].Height != height)
            { throw new ArgumentException("Butteraugli plane dimensions must match.", nameof(input)); }
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (float value in input.Planes[c].DangerousGetRowSpan(y))
                { if (!float.IsFinite(value)) { throw new ArgumentException("Butteraugli needs finite samples.", nameof(input)); } }
            }
        }
    }
}
