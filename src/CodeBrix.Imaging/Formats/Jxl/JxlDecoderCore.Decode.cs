// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Spot-color equations adapted from jxl-oxide crates/jxl-render/src/features/
// spot_colors.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

// Noise/upsampling stage order and preview noise indexing follow libjxl
// lib/jxl/dec_cache.cc and dec_frame.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).

using System;
using System.Buffers;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Metadata;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Jxl;

internal sealed partial class JxlDecoderCore
{
    internal Image<TPixel> Decode<TPixel>(Configuration configuration, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        List<ImageFrame<TPixel>> frames = [];
        try
        {
            (ImageMetadata metadata, _) = this.DecodeFrames(configuration, stream, true, false,
                (header, frame, rendered, _, color) => frames.Add(this.CreateDisplayFrame<TPixel>(configuration, header, frame, rendered, color, cancellationToken)), cancellationToken);
            Image<TPixel> image = new(configuration, metadata, frames);
            frames.Clear();
            return image;
        }
        finally { foreach (ImageFrame<TPixel> frame in frames) { frame.Dispose(); } }
    }

    private (ImageMetadata Metadata, JxlImageHeader Header) DecodeFrames(Configuration configuration, Stream stream, bool coalesce, bool raw,
        Action<JxlImageHeader, JxlFrameHeader, JxlFloatFrame, JxlModularFrame, JxlOutputColorTransform> accept, CancellationToken cancellationToken,
        Action<JxlImageHeader, ImageMetadata> onHeader = null, bool retainFrames = true)
    {
        JxlDecodeBudget budget = new(this.options);
        using JxlCodestreamStream codestream = new(stream, this.options.MaxMetadataBytes, this.options.IgnoreMetadata, cancellationToken);
        JxlBitReader reader = new(codestream, cancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(reader, this.options.MaxPixels, this.options.MaxMetadataBytes);
        byte[] icc = header.Color.WantIcc
            ? JxlIccCodec.Decode(JxlIccCodec.ReadEncoded(reader, this.options.MaxMetadataBytes, configuration.MemoryAllocator), this.options.MaxMetadataBytes)
            : null;
        JxlOutputColorTransform outputColor = new(header, icc, this.options, raw);
        if (onHeader != null)
        { onHeader(header, this.CreateMetadata(header, codestream, outputColor.OutputSrgb ? null : icc, 0, originalColor: !outputColor.OutputSrgb)); }
        if (header.Preview.HasValue)
        {
            budget.CountFrame();
            JxlFrameHeader preview = JxlFrameHeader.Read(reader, header.ForPreview(), this.options.MaxPixels, this.options.MaxMetadataBytes);
            JxlFrameToc previewToc = JxlFrameToc.Read(reader, preview, configuration.MemoryAllocator);
            reader.SkipBits(previewToc.TotalBytes * 8);
        }
        // A skipped preview still occupies an invisible frame index. Noisy
        // references before the first displayed frame use this in their seed.
        int displayFrames = 0, invisibleFrames = header.Preview.HasValue ? 1 : 0;
        JxlFloatFrame[] references = new JxlFloatFrame[4], lfReferences = new JxlFloatFrame[5];
        try
        {
            while (true)
            {
                budget.CountFrame();
                JxlFrameHeader frame = JxlFrameHeader.Read(reader, header, this.options.MaxPixels, this.options.MaxMetadataBytes);
                budget.ReserveFrame(header, frame, coalesce, retainFrames);
                JxlFrameToc toc = JxlFrameToc.Read(reader, frame, configuration.MemoryAllocator);
                using JxlModularWorkspace workspace = new(configuration.MemoryAllocator, this.options.MaxPixels, this.options.MaxModularBytes);
                JxlModularFrame modular = frame.Modular ? JxlModularFrame.Read(reader, header, frame, toc, workspace, cancellationToken, configuration.MaxDegreeOfParallelism, budget) : null;
                using JxlVarDctFrame vardct = frame.Modular ? null : JxlVarDctFrame.Read(reader, header, frame, toc, workspace, cancellationToken,
                    frame.UseLfFrame ? lfReferences[frame.LfLevel + 1] : null, maxDegreeOfParallelism: configuration.MaxDegreeOfParallelism, budget: budget);
                JxlFloatFrame rendered = modular != null ? JxlFloatFrame.FromModular(modular, header, configuration.MemoryAllocator) : vardct.TakeFrame();
                JxlPatches patches = modular?.Patches ?? vardct?.Patches;
                invisibleFrames = frame.IsKeyframe ? 0 : invisibleFrames + 1;
                try
                {
                    if (frame.YCbCr) { JxlUpsampling.ApplyJpeg(rendered, frame, cancellationToken); }
                    JxlRestoration.Apply(rendered, frame.Restoration, cancellationToken, vardct?.Sigma, configuration.MaxDegreeOfParallelism);
                    if (patches != null)
                    {
                        JxlUpsampling.Apply(rendered, header, frame, true, cancellationToken);
                        patches.Apply(header, rendered, references, cancellationToken);
                    }
                    (modular?.Splines ?? vardct?.Splines)?.Apply(rendered, vardct?.CorrelationX ?? 0, vardct?.CorrelationB ?? 1, cancellationToken);
                    JxlUpsampling.Apply(rendered, header, frame, false, cancellationToken);
                    (modular?.Noise ?? vardct?.Noise)?.Apply(rendered, frame, displayFrames + (frame.IsKeyframe ? 1 : 0), invisibleFrames,
                        vardct?.CorrelationX ?? 0, vardct?.CorrelationB ?? 1, cancellationToken);
                    // Copy requested coded planes before converting the working
                    // layer used for composition and future frame references.
                    if (outputColor.PreserveCoded) { accept(header, frame, rendered, modular ?? vardct?.RawSources, outputColor); }
                    if (!frame.SaveBeforeColorTransform && !rendered.ColorTransformed)
                    {
                        // A terminal replacement frame needs no encoded-color intermediate for
                        // blending or references. Keep XYB output linear through tone mapping.
                        bool linearOutput = outputColor.OutputSrgb && !frame.CanReference && frame.Blend.Mode == 0 &&
                            Array.TrueForAll(frame.ExtraBlending, b => b.Mode == 0);
                        new JxlColorTransform(header, frame.YCbCr, linearOutput).Apply(rendered, cancellationToken);
                    }
                    if (!coalesce && !outputColor.PreserveCoded) { accept(header, frame, rendered, modular ?? vardct?.RawSources, outputColor); }
                    if (frame.IsNormal)
                    {
                        JxlFloatFrame composed = JxlCompositor.Compose(header, frame, rendered, references, cancellationToken, allowReuseLayer: true);
                        if (!ReferenceEquals(composed, rendered)) { rendered.Dispose(); }
                        rendered = composed;
                    }
                    if (frame.IsKeyframe)
                    {
                        if (coalesce) { accept(header, frame, rendered, modular ?? vardct?.RawSources, outputColor); }
                        displayFrames++;
                    }
                    if (frame.FrameType == 1)
                    {
                        lfReferences[frame.LfLevel]?.Dispose();
                        lfReferences[frame.LfLevel] = rendered;
                        rendered = null;
                    }
                    if (frame.CanReference)
                    {
                        references[frame.SaveAsReference]?.Dispose();
                        references[frame.SaveAsReference] = rendered;
                        rendered = null;
                    }
                }
                finally { rendered?.Dispose(); }
                if (frame.IsLast || displayFrames >= this.options.MaxFrames || (displayFrames != 0 && this.options.DecodingMode == FrameDecodingMode.First)) { break; }
            }
            codestream.Drain();
            return (this.CreateMetadata(header, codestream, outputColor.OutputSrgb ? null : icc, displayFrames, coalesce && this.options.ApplyOrientation, !outputColor.OutputSrgb), header);
        }
        finally
        {
            foreach (JxlFloatFrame reference in references) { reference?.Dispose(); }
            foreach (JxlFloatFrame reference in lfReferences) { reference?.Dispose(); }
        }
    }

    private ImageFrame<TPixel> CreateDisplayFrame<TPixel>(Configuration configuration, JxlImageHeader header,
        JxlFrameHeader frame, JxlFloatFrame rendered, JxlOutputColorTransform outputColor, CancellationToken cancellationToken,
        ImageFrame<TPixel> destination = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int orientation = this.options.ApplyOrientation ? (int)header.Orientation : 1;
        int width = (int)header.Size.Width, height = (int)header.Size.Height;
        int outputWidth = orientation > 4 ? height : width, outputHeight = orientation > 4 ? width : height;
        ImageFrame<TPixel> image = destination ?? new(configuration, outputWidth, outputHeight);
        try
        {
            JxlColorTransform transform = rendered.ColorTransformed ? null : new(header, frame.YCbCr);
            JxlFrameMetadata frameMetadata = image.Metadata.GetJxlMetadata();
            CopyFrameMetadata(frame, frameMetadata);
            int alphaIndex = Array.FindIndex(header.ExtraChannels, extra => extra.Type == 0);
            int alphaChannel = alphaIndex < 0 ? -1 : rendered.ColorChannels + alphaIndex;
            bool associated = alphaIndex >= 0 && header.ExtraChannels[alphaIndex].AlphaAssociated;
            if (alphaChannel >= 0 && (rendered.Planes[alphaChannel].Width != width || rendered.Planes[alphaChannel].Height != height))
            { throw new NotSupportedException("JPEG XL extra-channel resampling is under development."); }
            bool paintSpots = this.options.RenderSpotColors && rendered.ColorChannels == 3 && Array.Exists(header.ExtraChannels, e => e.Type == 2);
            bool identity = transform == null && outputColor.IsIdentity(rendered);
            using IMemoryOwner<Vector4> rowMemory = orientation == 1 ? configuration.MemoryAllocator.Allocate<Vector4>(width) : null;
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> red = rendered.Planes[0].DangerousGetRowSpan(y);
                Span<float> green = rendered.Planes[rendered.ColorChannels == 1 ? 0 : 1].DangerousGetRowSpan(y);
                Span<float> blue = rendered.Planes[rendered.ColorChannels == 1 ? 0 : 2].DangerousGetRowSpan(y);
                Span<float> alpha = alphaChannel < 0 ? Span<float>.Empty : rendered.Planes[alphaChannel].DangerousGetRowSpan(y);
                Span<float> blacks = outputColor.BlackIndex < 0 ? default : rendered.Planes[rendered.ColorChannels + outputColor.BlackIndex].DangerousGetRowSpan(y);
                Span<Vector4> vectors = rowMemory == null ? default : rowMemory.Memory.Span;
                for (int x = 0; x < width; x++)
                {
                    Vector4 value = new(red[x], green[x], blue[x], alpha.IsEmpty ? 1 : alpha[x]);
                    if (transform != null)
                    {
                        Vector3 converted = transform.Convert(new(value.X, value.Y, value.Z));
                        value.X = converted.X; value.Y = converted.Y; value.Z = converted.Z;
                    }
                    if (paintSpots)
                    {
                        for (int c = 0; c < header.ExtraChannels.Length; c++)
                        {
                            JxlExtraChannel extra = header.ExtraChannels[c];
                            if (extra.Type != 2) { continue; }
                            float mix = rendered.Planes[rendered.ColorChannels + c].DangerousGetRowSpan(y)[x] * extra.SpotColor[3];
                            value.X = (mix * extra.SpotColor[0]) + ((1 - mix) * value.X);
                            value.Y = (mix * extra.SpotColor[1]) + ((1 - mix) * value.Y);
                            value.Z = (mix * extra.SpotColor[2]) + ((1 - mix) * value.Z);
                        }
                    }
                    if (!identity)
                    {
                        // The frame transform was applied above, before painting spot colors.
                        Vector3 output = outputColor.Convert(new(value.X, value.Y, value.Z), blacks.IsEmpty ? 0 : blacks[x], rendered, null, transform != null && header.XybEncoded && header.Color.WantIcc);
                        value.X = output.X; value.Y = output.Y; value.Z = output.Z;
                    }
                    if (associated && value.W > 0) { value.X /= value.W; value.Y /= value.W; value.Z /= value.W; }
                    if (orientation == 1) { vectors[x] = value; continue; }
                    (int dx, int dy) = Orient(x, y, width, height, orientation);
                    TPixel pixel = default;
                    if (typeof(TPixel) == typeof(RgbaVector))
                    {
                        RgbaVector floating = new(value.X, value.Y, value.Z, value.W);
                        pixel = Unsafe.As<RgbaVector, TPixel>(ref floating);
                    }
                    else if (IsHalfPixel<TPixel>()) { pixel.FromVector4(value); }
                    else if (IsNormalizedPixel<TPixel>()) { pixel = PackNormalized<TPixel>(value); }
                    else { pixel.FromScaledVector4(value); }
                    image.PixelBuffer.DangerousGetRowSpan(dy)[dx] = pixel;
                }
                if (orientation == 1)
                {
                    Span<TPixel> pixels = image.PixelBuffer.DangerousGetRowSpan(y);
                    if (IsNormalizedPixel<TPixel>())
                    { for (int x = 0; x < width; x++) { pixels[x] = PackNormalized<TPixel>(vectors[x]); } }
                    else
                    { PixelOperations<TPixel>.Instance.FromVector4Destructive(configuration, vectors, pixels,
                        IsHalfPixel<TPixel>() ? PixelConversionModifiers.None : PixelConversionModifiers.Scale); }
                }
            }
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    private static bool IsHalfPixel<TPixel>() => typeof(TPixel) == typeof(HalfSingle) || typeof(TPixel) == typeof(HalfVector2) || typeof(TPixel) == typeof(HalfVector4);

    private static bool IsNormalizedPixel<TPixel>() => typeof(TPixel) == typeof(NormalizedByte2) || typeof(TPixel) == typeof(NormalizedByte4)
        || typeof(TPixel) == typeof(NormalizedShort2) || typeof(TPixel) == typeof(NormalizedShort4);

    private static TPixel PackNormalized<TPixel>(Vector4 value) where TPixel : unmanaged, IPixel<TPixel>
    {
        // These signed formats divide by the positive maximum when exporting samples.
        // Their most-negative integer therefore lies slightly below scaled zero. The
        // usual pixel conversion clamps it away; retain it when packing lossless JXL.
        bool bytes = typeof(TPixel) == typeof(NormalizedByte2) || typeof(TPixel) == typeof(NormalizedByte4);
        int bits = bytes ? 8 : 16;
        int maximum = (1 << (bits - 1)) - 1;
        ulong mask = (1UL << bits) - 1;
        ulong packed = 0;
        int count = typeof(TPixel) == typeof(NormalizedByte2) || typeof(TPixel) == typeof(NormalizedShort2) ? 2 : 4;
        for (int c = 0; c < count; c++)
        {
            float sample = ((value[c] * 2) - 1) * maximum;
            int integer = float.IsNaN(sample) ? 0 : (int)Math.Clamp(MathF.Round(sample), -maximum - 1, maximum);
            packed |= (unchecked((ulong)integer) & mask) << (c * bits);
        }
        if (typeof(TPixel) == typeof(NormalizedByte2))
        { NormalizedByte2 pixel = new() { PackedValue = (ushort)packed }; return Unsafe.As<NormalizedByte2, TPixel>(ref pixel); }
        if (typeof(TPixel) == typeof(NormalizedByte4))
        { NormalizedByte4 pixel = new() { PackedValue = (uint)packed }; return Unsafe.As<NormalizedByte4, TPixel>(ref pixel); }
        if (typeof(TPixel) == typeof(NormalizedShort2))
        { NormalizedShort2 pixel = new() { PackedValue = (uint)packed }; return Unsafe.As<NormalizedShort2, TPixel>(ref pixel); }
        NormalizedShort4 wide = new() { PackedValue = packed }; return Unsafe.As<NormalizedShort4, TPixel>(ref wide);
    }
}
