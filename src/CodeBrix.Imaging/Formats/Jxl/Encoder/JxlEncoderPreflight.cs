// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular.FastLossless;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder;

// Admission control for allocations that cannot all use MemoryAllocator (token
// arrays, histograms and retained compressed candidates). This is a deliberately
// conservative estimate of encoder-owned working storage, not a process RSS cap.
// Never change the requested effort, predictors or global search to fit a limit.
internal static class JxlEncoderPreflight
{
    internal static long MetadataBytes(byte[] icc, byte[] exif, byte[] xmp, byte[] jumbf)
        => 8L * (icc?.LongLength ?? 0) + 4L * ((exif?.LongLength ?? 0) + (xmp?.LongLength ?? 0) + (jumbf?.LongLength ?? 0));

    internal static long Features(JxlRawFrameEncoding source, int extraChannels, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        if (source.Patches == null || source.Splines == null || source.QuantizationMatrices == null)
        { throw new ArgumentException("Feature arrays cannot be null.", nameof(source)); }
        long bytes = 0;
        foreach (JxlPatch patch in source.Patches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (patch?.Targets == null) { throw new ArgumentException("Invalid JPEG XL patch source.", nameof(source)); }
            Add(1, 512); Add(patch.Targets.LongLength, 512L + 256L * extraChannels);
        }
        foreach (JxlSpline spline in source.Splines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (spline?.ControlPoints == null) { throw new ArgumentException("Invalid JPEG XL spline.", nameof(source)); }
            Add(1, 16384); Add(spline.ControlPoints.LongLength, 256);
        }
        foreach (JxlQuantizationMatrix matrix in source.QuantizationMatrices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (matrix?.X == null || matrix.Y == null || matrix.B == null)
            { throw new ArgumentException("Invalid JPEG XL quantization matrix.", nameof(source)); }
            Add(matrix.X.LongLength + matrix.Y.LongLength + matrix.B.LongLength, 128);
        }
        return bytes;

        void Add(long count, long size)
        {
            try { bytes = checked(bytes + count * size); }
            catch (OverflowException)
            { throw new InvalidMemoryOperationException("JPEG XL feature-memory estimate exceeds the supported range."); }
            Require(bytes, allocator);
        }
    }

    internal static void Displayed(JxlImageHeader image, JxlEncoder options, MemoryAllocator allocator, long metadataBytes)
    {
        JxlFrameHeader frame = new()
        {
            Modular = options.IsLossless, Width = image.Size.Width, Height = image.Size.Height,
            GroupSizeShift = options.IsLossless && options.FasterDecoding >= 2 ? 0U : 1U,
            Upsampling = (uint)options.Resampling,
            ExtraUpsampling = image.ExtraChannels.Length == 0 ? [] : [(uint)options.Resampling],
        };
        Frame(image, frame, options, allocator, (int)image.Size.Width, (int)image.Size.Height, true, false, metadataBytes);
    }

    internal static void Frame(JxlImageHeader image, JxlFrameHeader frame, JxlEncoder options, MemoryAllocator allocator,
        int sourceWidth, int sourceHeight, bool displayed, bool preview, long metadataBytes)
    {
        long rowWidth = sourceWidth;
        if (displayed && options.IsLossless && options.Progressive == JxlProgressiveMode.None)
        {
            if (options.Effort == 1 && JxlFastLosslessEncoder.CanEncode(image, frame)) { rowWidth = Math.Min(rowWidth, 256); }
            else if (JxlModularEncoder.ShouldChunk(frame, options.Effort)) { rowWidth = Math.Min(rowWidth, frame.GroupDimension * 8); }
        }
        // Pixel import uses Vector4 rows; weighted prediction uses four-int
        // error rows with a small border. These buffers must be contiguous even
        // when the caller raises the aggregate group limit above one GiB.
        if ((rowWidth + 2) * 16 > allocator.SingleBufferAllocationLimitBytes)
        { throw new InvalidMemoryOperationException("JPEG XL encoder row scratch exceeds the configured contiguous-buffer limit."); }
        Require(Estimate(image, frame, options, sourceWidth, sourceHeight, displayed, preview, metadataBytes), allocator);
    }

    internal static long Estimate(JxlImageHeader image, JxlFrameHeader frame, JxlEncoder options,
        int sourceWidth, int sourceHeight, bool displayed, bool preview, long metadataBytes)
    {
        JxlProgressiveMode progression = preview ? JxlProgressiveMode.None : options.Progressive;
        bool modular = options.IsLossless || options.Modular == true;
        bool expert = displayed && options.IsLossless && options.Effort == 11 && progression == JxlProgressiveMode.None;
        uint minimumShift = modular && (options.FasterDecoding >= 2 || expert) ? 0 : frame.GroupSizeShift;
        uint passes = frame.FrameType != 2 && progression is JxlProgressiveMode.Ac or JxlProgressiveMode.Full ? 3 : frame.PassCount;
        long sections = ValidateToc(frame, minimumShift, passes);
        try
        {
            checked
            {
                long pixels = (long)sourceWidth * sourceHeight;
                long channels = (long)image.ColorChannels + image.ExtraChannels.Length, samples = pixels * channels;
                if (!options.IsLossless && options.Modular != true)
                {
                    // Full-size imported/reference pixels coexist with quantized
                    // coefficients, AC tokens, output sections and (effort 8+)
                    // multiscale Butteraugli reference/test/filter storage.
                    // Calibrated at about 1.3x measured peak working sets
                    // (effort 7: 35-48 bytes/pixel; effort 8: 210-240 bytes/pixel).
                    long padded = ((sourceWidth + 7L) / 8 * 8) * ((sourceHeight + 7L) / 8 * 8);
                    return metadataBytes + (4L << 20) + padded * (options.Effort >= 8 ? 288 : 64)
                        + pixels * image.ExtraChannels.Length * 48 + Math.Min(128L << 20, samples * 256) + sections * 4096;
                }
                bool fast = displayed && options.IsLossless && progression == JxlProgressiveMode.None && options.Effort == 1
                    && JxlFastLosslessEncoder.CanEncode(image, frame);
                bool chunked = !fast && displayed && options.IsLossless && progression == JxlProgressiveMode.None
                    && JxlModularEncoder.ShouldChunk(frame, options.Effort);
                int dimension = expert ? 1024 : (int)(128U << (int)minimumShift);
                long groupSamples = (long)Math.Min(sourceWidth, dimension) * Math.Min(sourceHeight, dimension) * channels;
                long imported = fast ? groupSamples : chunked
                    ? (long)Math.Min(sourceWidth, dimension * 8) * Math.Min(sourceHeight, dimension * 8) * channels : samples;
                // At most 32 residual bits plus entropy code and framing per
                // sample. Retained palettes, best/current candidates and stream
                // capacity growth are separate copies, not extra search passes.
                long payload = samples * 8 + sections * Math.Min(1L << 20, groupSamples * 8 + 4096);
                long copies = fast || chunked ? 1 : expert ? 7 : 4;
                long working = imported * (expert ? 12 : 8);
                if (!options.IsLossless) { working += pixels * (3 + image.ExtraChannels.Length) * 16; }
                bool patches = displayed && !fast && !chunked && options.IsLossless && options.Patches != false
                    && options.Effort >= 5 && image.BitDepth.ExponentBits == 0 && image.BitDepth.Bits <= 16;
                if (patches) { copies += 4; working += samples * 8; }
                if (displayed && options.IsLossless && options.Effort >= 10 && progression == JxlProgressiveMode.None)
                { working += samples * 16; } // Global literals and matched tokens.
                long scratch = fast ? groupSamples * 8 + (64 << 10)
                    : (1L << 20) + groupSamples * 96 + Math.Min(128L << 20, imported * 256);
                return metadataBytes + working + payload * copies + scratch;
            }
        }
        catch (OverflowException)
        { throw new InvalidMemoryOperationException("JPEG XL encoder working-memory estimate exceeds the supported range."); }
    }

    internal static long ValidateToc(JxlFrameHeader frame, uint groupShift, uint passes)
    {
        uint dimension = 128U << (int)groupShift;
        ulong groups = (ulong)JxlFrameHeader.DivideRoundUp(frame.SampleWidth, dimension)
            * JxlFrameHeader.DivideRoundUp(frame.SampleHeight, dimension);
        ulong lfGroups = (ulong)JxlFrameHeader.DivideRoundUp(frame.SampleWidth, dimension * 8)
            * JxlFrameHeader.DivideRoundUp(frame.SampleHeight, dimension * 8);
        ulong sections = groups == 1 && passes == 1 ? 1 : 2 + lfGroups + groups * passes;
        if (sections > 65536)
        { throw new ArgumentException("JPEG XL encoding requires more than 65,536 frame sections at the requested group size and progression.", nameof(frame)); }
        return (long)sections;
    }

    internal static long JpegBytes(long coefficientSamples, long inputBytes, long tokens)
    {
        // Four bytes per coefficient-plane sample (exact, including MCU padding
        // and grayscale's zero chroma planes), buffered input and its metadata/
        // padding copies, and sixteen bytes per retained AC token: the eight-byte
        // token plus its share of encoded sections held until the TOC is written.
        // Tokens depend on content, so callers add them as groups are tokenized.
        // JPEG has no full-frame candidate or perceptual-comparison search.
        try { return checked((4L << 20) + coefficientSamples * 4 + inputBytes * 8 + tokens * 16); }
        catch (OverflowException)
        { throw new InvalidMemoryOperationException("JPEG XL encoder working-memory estimate exceeds the supported range."); }
    }

    internal static void Require(long bytes, MemoryAllocator allocator)
    {
        if (bytes > allocator.MemoryGroupAllocationLimitBytes)
        {
            throw new InvalidMemoryOperationException($"JPEG XL encoding estimates {bytes} bytes of working storage, exceeding the configured allocator limit of {allocator.MemoryGroupAllocationLimitBytes} bytes. Increase MemoryAllocatorOptions.AllocationLimitMegabytes to permit this encoding.");
        }
    }
}
