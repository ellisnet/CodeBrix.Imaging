// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Original integration of the separately attributed managed ICC and XYB ports.

using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using CodeBrix.Imaging.ColorSpaces.Icc;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal sealed class JxlVarDctInput
{
    private static readonly IccMatrix XyzToSrgb = IccColorProfile.SrgbToXyzD50.Inverse();
    private readonly IccColorProfile profile;
    private readonly JxlEnumeratedColorSpace enumerated;
    private readonly JxlXybEncoder xyb;
    private readonly JxlImageHeader header;

    internal JxlVarDctInput(JxlImageHeader header, byte[] icc)
    {
        this.header = header;
        this.xyb = new(header);
        if (icc == null) { this.enumerated = new(header.Color, header.IntensityTarget); }
        else
        {
            this.profile = IccColorProfile.Read(icc, header.Color.RenderingIntent);
            if (this.profile.Channels != (header.Color.ColorSpace == 1 ? 1 : 3))
            { throw new ArgumentException("The ICC profile does not match the image's color channels.", nameof(icc)); }
        }
    }

    internal float MetricIntensity => (this.profile?.HdrTransfer ?? this.header.Color.TransferFunction) is 16 or 18
        ? this.header.IntensityTarget : 80;

    internal JxlFloatFrame ImportRaw(JxlRawFrame source, Configuration configuration, CancellationToken cancellationToken, JxlFloatFrame linearReference)
    {
        JxlFloatFrame result = new(configuration.MemoryAllocator, 3, 3);
        try
        {
            for (int c = 0; c < 3; c++) { result.Planes[c] = configuration.MemoryAllocator.Allocate2D<float>(source.Width, source.Height); }
            JxlColorTransform fromXyb = source.ColorSpace == JxlRawColorSpace.Xyb ? new(this.header, false, true) : null;
            Span<float> channels = stackalloc float[3];
            for (int y = 0; y < source.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < source.Width; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        JxlRawPlane plane = source.Planes[source.ColorChannelCount == 1 ? 0 : c];
                        channels[c] = plane.SampleType == JxlSampleType.FloatingPoint ? plane.GetFloatRowSpan(y)[x]
                            : JxlSampleConverter.ToFloat(plane.GetIntegerRowSpan(y)[x], new((uint)plane.BitsPerSample, 0));
                        if (!float.IsFinite(channels[c])) { throw new ArgumentException("Perceptual JPEG XL encoding requires finite color samples.", nameof(source)); }
                    }
                    Vector3 value = new(channels[0], channels[1], channels[2]), linear, sample;
                    if (fromXyb != null) { sample = value; linear = fromXyb.Convert(value); }
                    else
                    {
                        linear = this.profile == null ? this.enumerated.ToLinearSrgb(value) : XyzToSrgb.Apply(this.profile.ToXyz(channels));
                        if (this.profile?.HdrTransfer != 0 && this.profile != null) { linear *= 203 / this.header.IntensityTarget; }
                        sample = this.xyb.FromLinearSrgb(linear);
                    }
                    for (int c = 0; c < 3; c++)
                    {
                        result.Planes[c].DangerousGetRowSpan(y)[x] = sample[c];
                        if (linearReference != null) { linearReference.Planes[c].DangerousGetRowSpan(y)[x] = linear[c]; }
                    }
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal JxlFloatFrame Import<TPixel>(ImageFrame<TPixel> source, JxlModularPlane[] extras, Configuration configuration,
        JxlTransparentColorMode transparentColorMode, CancellationToken cancellationToken, JxlFloatFrame linearReference = null) where TPixel : unmanaged, IPixel<TPixel>
    {
        JxlFloatFrame result = new(configuration.MemoryAllocator, 3, 3);
        try
        {
            for (int c = 0; c < 3; c++) { result.Planes[c] = configuration.MemoryAllocator.Allocate2D<float>(source.Width, source.Height); }
            using IMemoryOwner<Vector4> owner = configuration.MemoryAllocator.Allocate<Vector4>(source.Width);
            Span<Vector4> row = owner.Memory.Span[..source.Width];
            Span<float> channels = stackalloc float[3];
            bool half = this.header.BitDepth.ExponentBits == 5;
            for (int y = 0; y < source.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<TPixel> pixels = source.PixelBuffer.DangerousGetRowSpan(y);
                PixelOperations<TPixel>.Instance.ToVector4(configuration, pixels, row, half ? PixelConversionModifiers.None : PixelConversionModifiers.Scale);
                Span<float> xr = result.Planes[0].DangerousGetRowSpan(y), yr = result.Planes[1].DangerousGetRowSpan(y), br = result.Planes[2].DangerousGetRowSpan(y);
                for (int x = 0; x < source.Width; x++)
                {
                    Vector4 value = row[x];
                    if (extras.Length != 0)
                    {
                        JxlBitDepth depth = this.header.ExtraChannels[0].BitDepth;
                        extras[0].Set(x, y, depth.ExponentBits == 5 ? (ushort)(Unsafe.As<TPixel, HalfVector4>(ref pixels[x]).PackedValue >> 48)
                            : depth.ExponentBits != 0 ? BitConverter.SingleToInt32Bits(value.W)
                            : (int)MathF.Round(Math.Clamp(value.W, 0, 1) * ((1U << (int)depth.Bits) - 1)));
                        if (value.W == 0 && transparentColorMode == JxlTransparentColorMode.Clear) { value.X = value.Y = value.Z = 0; }
                    }
                    if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
                    { throw new ArgumentException("Perceptual JPEG XL encoding requires finite color samples.", nameof(source)); }
                    Vector3 linear;
                    if (this.profile == null) { linear = this.enumerated.ToLinearSrgb(new(value.X, value.Y, value.Z)); }
                    else
                    {
                        channels[0] = value.X; channels[1] = value.Y; channels[2] = value.Z;
                        linear = XyzToSrgb.Apply(this.profile.ToXyz(channels));
                        if (this.profile.HdrTransfer != 0) { linear *= 203 / this.header.IntensityTarget; }
                    }
                    if (linearReference != null)
                    {
                        linearReference.Planes[0].DangerousGetRowSpan(y)[x] = linear.X;
                        linearReference.Planes[1].DangerousGetRowSpan(y)[x] = linear.Y;
                        linearReference.Planes[2].DangerousGetRowSpan(y)[x] = linear.Z;
                    }
                    Vector3 sample = this.xyb.FromLinearSrgb(linear); xr[x] = sample.X; yr[x] = sample.Y; br[x] = sample.Z;
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
