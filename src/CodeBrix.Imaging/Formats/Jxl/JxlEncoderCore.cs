// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Formats.Gif;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Encoder;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular.FastLossless;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Metadata;
using CodeBrix.Imaging.Metadata.Profiles.Exif;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Jxl;

internal sealed class JxlEncoderCore(JxlEncoder options) : IImageEncoderInternals
{
    public void Encode<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Configuration configuration = image.GetConfiguration();
        using JxlParallel.Scope parallel = JxlParallel.EncoderScope(configuration.MaxDegreeOfParallelism);
        // Without JPEG XL metadata, an animated GIF source supplies its timing: GIF
        // delays are hundredths of a second, matching the default 100 ticks/second,
        // and both formats use zero for an infinite loop count.
        bool hasJxlMetadata = image.Metadata.TryGetFormatMetadata(JxlFormat.Instance, out _);
        GifMetadata gifSource = null;
        if (!hasJxlMetadata) { image.Metadata.TryGetFormatMetadata(GifFormat.Instance, out gifSource); }
        uint?[] gifDurations = new uint?[image.Frames.Count];
        for (int index = 0; gifSource != null && index < gifDurations.Length; index++)
        {
            ImageFrameMetadata frameMetadata = image.Frames[index].Metadata;
            if (!frameMetadata.TryGetFormatMetadata(JxlFormat.Instance, out _)
                && frameMetadata.TryGetFormatMetadata(GifFormat.Instance, out GifFrameMetadata gifFrame))
            { gifDurations[index] = (uint)Math.Max(gifFrame.FrameDelay, 0); }
        }
        JxlMetadata metadata = image.Metadata.GetJxlMetadata();
        PixelTypeInfo pixel = PixelOperations<TPixel>.Instance.GetPixelTypeInfo();
        bool gray = typeof(TPixel) == typeof(L8) || typeof(TPixel) == typeof(L16) || typeof(TPixel) == typeof(La16) || typeof(TPixel) == typeof(La32);
        bool alpha = pixel.AlphaRepresentation != PixelAlphaRepresentation.None;
        JxlBitDepth depth = PixelDepth<TPixel>();
        byte[] icc = image.Metadata.IccProfile?.ToByteArray();
        if (icc != null && options.ColorEncodingSnapshot != null)
        { throw new ArgumentException("Choose an ICC profile or an enumerated color encoding.", nameof(image)); }
        if (icc != null)
        {
            if (icc.Length < 128) { throw new ArgumentException("The ICC profile is truncated.", nameof(image)); }
            if (icc.AsSpan(16, 4).SequenceEqual("RGB "u8)) { gray = false; }
            else if (icc.AsSpan(16, 4).SequenceEqual("GRAY"u8))
            {
                if (!gray && !this.HasNeutralSamples(image, configuration, alpha, cancellationToken))
                { throw new ArgumentException("A grayscale ICC profile requires equal red, green and blue samples in every frame.", nameof(image)); }
                gray = true;
            }
            else
            { throw new ArgumentException("The ICC color space must match the image's RGB or grayscale samples.", nameof(image)); }
        }
        uint exifOrientation = image.Metadata.ExifProfile?.GetValue(ExifTag.Orientation)?.Value ?? 0U;
        // Invalid legacy Exif values (commonly zero) carry no orientation.
        // Preserve explicit JPEG XL metadata or its normal-orientation default.
        uint orientation = exifOrientation is >= 1 and <= 8 ? exifOrientation : (uint)metadata.Orientation;
        if (orientation is < 1 or > 8) { throw new ArgumentException("JPEG XL orientation must be from one through eight.", nameof(image)); }
        if (metadata.TicksPerSecondNumerator is 0 or > 1 << 30 || metadata.TicksPerSecondDenominator is 0 or > 1024 ||
            !float.IsFinite(metadata.IntensityTarget) || (float)(Half)metadata.IntensityTarget <= 0 || metadata.IntensityTarget > 65504)
        { throw new ArgumentException("Invalid JPEG XL animation or intensity metadata.", nameof(image)); }
        if (metadata.Level is not (5 or 10)) { throw new ArgumentException("JPEG XL level must be five or ten.", nameof(image)); }
        JxlImageHeader header = new()
        {
            Size = new((uint)image.Width, (uint)image.Height), XybEncoded = !options.IsLossless, Orientation = orientation,
            BitDepth = depth, Modular16BitBuffers = depth.Bits <= 12, IntensityTarget = (float)(Half)metadata.IntensityTarget,
            Color = options.ColorEncodingSnapshot is { } encoding ? encoding.ToHeader(gray ? 1U : 0) : new() { ColorSpace = gray ? 1U : 0, WantIcc = icc != null },
            ExtraChannels = alpha ? [new() { Type = 0, BitDepth = depth, AlphaAssociated = pixel.AlphaRepresentation == PixelAlphaRepresentation.Associated }] : [],
            IsAnimated = image.Frames.Count > 1 || metadata.IsAnimated, TpsNumerator = metadata.TicksPerSecondNumerator,
            TpsDenominator = metadata.TicksPerSecondDenominator, LoopCount = gifSource?.RepeatCount ?? metadata.LoopCount,
        };
        byte[] exif = image.Metadata.ExifProfile?.ToByteArray(), xmp = image.Metadata.XmpProfile?.Data;
        JxlEncoderPreflight.Displayed(header, options, configuration.MemoryAllocator,
            JxlEncoderPreflight.MetadataBytes(icc, exif, xmp, metadata.Jumbf));
        byte[] encodedIcc = icc == null ? null : JxlIccCodec.Encode(icc, cancellationToken);
        JxlVarDctInput lossyInput = options.IsLossless ? null : new(header, icc);
        foreach (ImageFrame<TPixel> frame in image.Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JxlFrameMetadata info = frame.Metadata.GetJxlMetadata();
            JxlName.GetByteCount(info.Name ?? string.Empty);
            header.HaveTimecodes |= info.Timecode != 0;
        }
        int level = Math.Max(metadata.Level, icc is { Length: > 1 << 22 } || depth.Bits > 16 || (options.Modular == true && !options.IsLossless && alpha && depth.Bits > 12)
            || image.Width > 262144 || image.Height > 262144 || (long)image.Width * image.Height > 268435456 ? 10 : 5);
        bool hasBoxes = exif is { Length: > 0 } || xmp is { Length: > 0 } || metadata.Jumbf is { Length: > 0 };
        bool container = options.UseContainer ?? (hasBoxes || level == 10);
        if (!container && (hasBoxes || level == 10))
        { throw new ArgumentException("A JPEG XL container is required for this metadata or codestream level.", nameof(options.UseContainer)); }
        cancellationToken.ThrowIfCancellationRequested();
        if (container)
        {
            JxlContainerWriter.Start(stream, level, cancellationToken);
            if (exif is { Length: > 0 })
            {
                int skip = exif.AsSpan().StartsWith("Exif\0\0"u8) ? 6 : 0;
                byte[] payload = new byte[4 + exif.Length - skip]; exif.AsSpan(skip).CopyTo(payload.AsSpan(4));
                JxlContainerWriter.WriteMetadata(stream, "Exif"u8, payload, options.CompressMetadataBoxes, options.BrotliEffort, cancellationToken);
            }
            if (xmp is { Length: > 0 })
            { JxlContainerWriter.WriteMetadata(stream, "xml "u8, xmp, options.CompressMetadataBoxes, options.BrotliEffort, cancellationToken); }
            if (metadata.Jumbf is { Length: > 0 })
            { JxlContainerWriter.WriteMetadata(stream, "jumb"u8, metadata.Jumbf, options.CompressMetadataBoxes, options.BrotliEffort, cancellationToken); }
            JxlContainerWriter.BeginFinalCodestream(stream, cancellationToken);
        }
        JxlBitWriter writer = new(stream); header.Write(writer);
        if (encodedIcc != null) { JxlIccCodec.WriteEncoded(writer, encodedIcc, options.Effort, cancellationToken); }
        for (int index = 0; index < image.Frames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageFrame<TPixel> source = image.Frames[index]; JxlFrameMetadata info = source.Metadata.GetJxlMetadata();
            uint duration = gifDurations[index] ?? info.Duration;
            // Image<TPixel> holds displayed canvases. Layer/reference metadata
            // describes its source and must not be applied a second time.
            JxlFrameHeader frame = new()
            {
                Modular = options.IsLossless, Width = (uint)image.Width, Height = (uint)image.Height, IsLast = index == image.Frames.Count - 1,
                GroupSizeShift = options.IsLossless && options.FasterDecoding >= 2 ? 0U : 1U,
                Upsampling = (uint)options.Resampling, ExtraUpsampling = alpha ? [(uint)options.Resampling] : [], ExtraBlending = alpha ? [default] : [],
                Duration = header.IsAnimated ? Math.Max(duration, 1U) : 0, Timecode = info.Timecode, Name = info.Name ?? string.Empty,
                Restoration = new() { GaborEnabled = false, EpfIterations = 0 },
            };
            if (lossyInput != null)
            {
                using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(configuration.MemoryAllocator, (long)image.Width * image.Height);
                JxlModularPlane[] extras = alpha ? [workspace.Allocate(image.Width, image.Height)] : [];
                using JxlFloatFrame linearReference = options.Modular != true && options.Effort >= 8 && (options.Resampling == 1 || options.EffectiveDistance > 4 * options.Resampling)
                    ? JxlButteraugliKernels.Create(image.Width, image.Height, 3, configuration.MemoryAllocator) : null;
                using JxlFloatFrame xyb = lossyInput.Import(source, extras, configuration, options.TransparentColorMode, cancellationToken, linearReference);
                if (options.Resampling != 1)
                {
                    JxlDownsampling.Apply(xyb, options.Resampling, options.Effort, cancellationToken, sharpen: options.Modular != true);
                    extras = JxlDownsampling.ExtraChannels(extras, header, options.Resampling, workspace, cancellationToken);
                }
                if (options.Modular == true)
                {
                    JxlLossyModularEncoder.WriteFrame(writer, header, frame, xyb, extras, options.EffectiveDistance, options.Effort,
                        options.Progressive is JxlProgressiveMode.Ac or JxlProgressiveMode.Full, options.FasterDecoding, options.PhotonNoiseIso, configuration.MemoryAllocator, cancellationToken);
                }
                else
                {
                    JxlVarDctEncoder.WriteFrame(writer, header, frame, xyb, extras, options.EffectiveDistance, options.Effort,
                        configuration.MemoryAllocator, cancellationToken, progressive: options.Progressive is JxlProgressiveMode.Ac or JxlProgressiveMode.Full, photonNoiseIso: options.PhotonNoiseIso,
                        linearReference: linearReference, metricIntensity: lossyInput.MetricIntensity, decodingSpeed: options.FasterDecoding,
                        progressiveDc: options.Progressive is JxlProgressiveMode.Dc or JxlProgressiveMode.Full, noiseModeling: options.NoiseModeling,
                        automaticPatches: options.Patches ?? options.Effort >= 5,
                        automaticDots: options.Dots ?? (options.Effort >= 7 && options.EffectiveDistance >= 3));
                }
            }
            else if (options.Progressive != JxlProgressiveMode.None)
            {
                using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(configuration.MemoryAllocator, Math.Max(262144, (long)image.Width * image.Height));
                JxlModularPlane[] planes = new JxlModularPlane[header.ColorChannels + header.ExtraChannels.Length];
                for (int c = 0; c < planes.Length; c++) { planes[c] = workspace.Allocate(image.Width, image.Height); }
                this.Import(source, planes, header, configuration, cancellationToken, new(0, 0, image.Width, image.Height));
                JxlSqueezeEncoder squeeze = JxlSqueezeEncoder.CreateDefault(planes, workspace, cancellationToken);
                if (options.Progressive is JxlProgressiveMode.Ac or JxlProgressiveMode.Full)
                { frame.PassCount = 3; frame.PassShifts = [0, 0]; frame.Downsample = [4, 2]; frame.LastPass = [0, 1]; }
                JxlModularSearchOptions search = JxlModularSearchOptions.ForDecodingSpeed(null, options.FasterDecoding, options.Effort, frame.GroupSizeShift);
                JxlLossyModularEncoder.WriteChannelsFrame(writer, header, frame, squeeze.Planes, squeeze, null, options.Effort, search, 0, configuration.MemoryAllocator, cancellationToken);
            }
            else if (options.Effort == 1 && JxlFastLosslessEncoder.CanEncode(header, frame))
            {
                JxlFastLosslessEncoder.WriteFrame(writer, header, frame,
                    (bounds, planes) => this.Import(source, planes, header, configuration, cancellationToken, bounds),
                    configuration.MemoryAllocator, cancellationToken);
            }
            else if (JxlModularEncoder.ShouldChunk(frame, options.Effort))
            {
                JxlModularEncoder.WriteFrameChunked(writer, header, frame,
                    (bounds, planes) => this.Import(source, planes, header, configuration, cancellationToken, bounds),
                    cancellationToken, options.Effort, configuration.MemoryAllocator, options.FasterDecoding);
            }
            else
            {
                using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(configuration.MemoryAllocator, (long)image.Width * image.Height);
                JxlModularPlane[] planes = new JxlModularPlane[header.ColorChannels + header.ExtraChannels.Length];
                for (int c = 0; c < planes.Length; c++) { planes[c] = workspace.Allocate(image.Width, image.Height); }
                this.Import(source, planes, header, configuration, cancellationToken, new(0, 0, image.Width, image.Height));
                if (options.Patches == false || !JxlLosslessPatchEncoder.TryWrite(writer, header, frame, planes, options.Effort, configuration.MemoryAllocator, cancellationToken, options.FasterDecoding))
                { JxlModularEncoder.WriteFrame(writer, header, frame, planes, cancellationToken, options.Effort == 1, options.Effort, configuration.MemoryAllocator, decodingSpeed: options.FasterDecoding); }
            }
        }
        writer.ZeroPadToByte();
    }

    private static JxlBitDepth PixelDepth<TPixel>() where TPixel : unmanaged, IPixel<TPixel>
    {
        Type type = typeof(TPixel);
        if (type == typeof(L8) || type == typeof(La16) || type == typeof(Rgb24) || type == typeof(Bgr24) ||
            type == typeof(Rgba32) || type == typeof(Bgra32) || type == typeof(Argb32) || type == typeof(Abgr32) || type == typeof(Byte4))
        { return new(8, 0); }
        if (type == typeof(L16) || type == typeof(La32) || type == typeof(Rgb48) || type == typeof(Rgba64)) { return new(16, 0); }
        if (type == typeof(HalfSingle) || type == typeof(HalfVector2) || type == typeof(HalfVector4)) { return new(16, 5); }
        return new(32, 8);
    }

    private bool HasNeutralSamples<TPixel>(Image<TPixel> image, Configuration configuration, bool alpha, CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Loaded gray PNG/JPEG images commonly use an RGB(A) pixel buffer while
        // retaining their GRAY profile. Check every frame before output, using
        // bounded scratch and exact bits so signed zero/NaN payloads stay lossless.
        using IMemoryOwner<Vector4> owner = configuration.MemoryAllocator.Allocate<Vector4>(Math.Min(image.Width, 1024));
        PixelConversionModifiers modifiers = PixelDepth<TPixel>().ExponentBits == 5 ? PixelConversionModifiers.None : PixelConversionModifiers.Scale;
        foreach (ImageFrame<TPixel> frame in image.Frames)
        {
            for (int y = 0; y < frame.Height; y++)
            {
                Span<TPixel> pixels = frame.PixelBuffer.DangerousGetRowSpan(y);
                for (int x = 0; x < pixels.Length; x += owner.Memory.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Span<TPixel> part = pixels.Slice(x, Math.Min(owner.Memory.Length, pixels.Length - x));
                    Span<Vector4> values = owner.Memory.Span[..part.Length];
                    PixelOperations<TPixel>.Instance.ToVector4(configuration, part, values, modifiers);
                    for (int i = 0; i < values.Length; i++)
                    {
                        Vector4 value = values[i];
                        if (alpha && value.W == 0 && options.TransparentColorMode == JxlTransparentColorMode.Clear) { continue; }
                        if (typeof(TPixel) == typeof(HalfVector4))
                        {
                            ulong bits = Unsafe.As<TPixel, HalfVector4>(ref part[i]).PackedValue;
                            if ((ushort)bits != (ushort)(bits >> 16) || (ushort)bits != (ushort)(bits >> 32)) { return false; }
                        }
                        else if (BitConverter.SingleToInt32Bits(value.X) != BitConverter.SingleToInt32Bits(value.Y) ||
                            BitConverter.SingleToInt32Bits(value.X) != BitConverter.SingleToInt32Bits(value.Z)) { return false; }
                    }
                }
            }
        }
        return true;
    }

    private void Import<TPixel>(ImageFrame<TPixel> source, JxlModularPlane[] planes, JxlImageHeader header,
        Configuration configuration, CancellationToken cancellationToken, Rectangle bounds) where TPixel : unmanaged, IPixel<TPixel>
    {
        if (header.BitDepth.ExponentBits == 5)
        { this.ImportHalf(source, planes, header.ColorChannels == 1, cancellationToken, bounds); return; }
        using IMemoryOwner<Vector4> owner = configuration.MemoryAllocator.Allocate<Vector4>(bounds.Width);
        Span<Vector4> row = owner.Memory.Span[..bounds.Width]; bool gray = header.ColorChannels == 1;
        bool alpha = planes.Length > header.ColorChannels, floats = header.BitDepth.ExponentBits != 0;
        float maximum = floats ? 0 : (1U << (int)header.BitDepth.Bits) - 1;
        for (int y = 0; y < bounds.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PixelOperations<TPixel>.Instance.ToVector4(configuration, source.PixelBuffer.DangerousGetRowSpan(bounds.Top + y).Slice(bounds.Left, bounds.Width), row, PixelConversionModifiers.Scale);
            Span<int> a = planes[0].Row(y), b = gray ? default : planes[1].Row(y), c = gray ? default : planes[2].Row(y);
            Span<int> opacity = alpha ? planes[^1].Row(y) : default;
            for (int x = 0; x < row.Length; x++)
            {
                Vector4 value = row[x];
                if (alpha && value.W == 0 && options.TransparentColorMode == JxlTransparentColorMode.Clear)
                { value.X = value.Y = value.Z = 0; }
                a[x] = Sample(value.X);
                if (!gray) { b[x] = Sample(value.Y); c[x] = Sample(value.Z); }
                if (alpha) { opacity[x] = Sample(value.W); }
            }
        }
        int Sample(float value) => floats ? BitConverter.SingleToInt32Bits(value) : (int)MathF.Round(Math.Clamp(value, 0, 1) * maximum);
    }

    private void ImportHalf<TPixel>(ImageFrame<TPixel> source, JxlModularPlane[] planes, bool gray, CancellationToken cancellationToken, Rectangle bounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        bool four = typeof(TPixel) == typeof(HalfVector4), two = typeof(TPixel) == typeof(HalfVector2);
        for (int y = 0; y < bounds.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested(); Span<TPixel> row = source.PixelBuffer.DangerousGetRowSpan(bounds.Top + y).Slice(bounds.Left, bounds.Width);
            Span<int> red = planes[0].Row(y), green = gray ? default : planes[1].Row(y), blue = gray ? default : planes[2].Row(y);
            Span<int> alpha = four ? planes[^1].Row(y) : default;
            for (int x = 0; x < row.Length; x++)
            {
                ulong bits = four ? Unsafe.As<TPixel, HalfVector4>(ref row[x]).PackedValue :
                    two ? Unsafe.As<TPixel, HalfVector2>(ref row[x]).PackedValue : Unsafe.As<TPixel, HalfSingle>(ref row[x]).PackedValue;
                if (four && (bits & 0x7fff000000000000UL) == 0 && options.TransparentColorMode == JxlTransparentColorMode.Clear)
                { bits &= 0xffff000000000000UL; }
                red[x] = (ushort)bits;
                if (!gray) { green[x] = (ushort)(bits >> 16); blue[x] = (ushort)(bits >> 32); }
                if (four) { alpha[x] = (ushort)(bits >> 48); }
            }
        }
    }
}
