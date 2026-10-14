// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Encoder;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlEncoderMemoryLimitTests
{
    [Theory]
    [InlineData(true, 1, false, false)] [InlineData(true, 7, false, false)]
    [InlineData(true, 10, false, false)] [InlineData(true, 11, false, false)]
    [InlineData(true, 7, false, true)] [InlineData(false, 1, false, false)]
    [InlineData(false, 10, false, true)] [InlineData(false, 7, true, false)]
    public async Task displayed_sync_and_async_requests_reject_before_output(bool lossless, int effort, bool modular, bool progressive)
    {
        Configuration configuration = Limited(1);
        using Image<Rgba32> image = new(configuration, 256, 256);
        JxlEncoder encoder = Options(lossless, effort, modular, progressive);
        using MemoryStream output = Sentinel();
        InvalidMemoryOperationException error = Assert.Throws<InvalidMemoryOperationException>(() => encoder.Encode(image, output));
        Assert.Contains("working storage", error.Message); AssertSentinel(output);
        await Assert.ThrowsAsync<InvalidMemoryOperationException>(() => encoder.EncodeAsync(image, output, TestContext.Current.CancellationToken));
        AssertSentinel(output);
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(false, true)]
    public void raw_later_frames_reject_before_the_first_frame_is_written(bool lossless, bool modular)
    {
        using JxlRawImage image = new(256, 256,
            [new([JxlRawPlane.FromIntegers(1, 1, [12], 8)], JxlRawColorSpace.Grayscale),
             new([JxlRawPlane.FromIntegers(256, 256, new int[256 * 256], 8)], JxlRawColorSpace.Grayscale)]);
        image.Frames[0].Metadata.Duration = 1;
        using MemoryStream output = Sentinel();
        Assert.Throws<InvalidMemoryOperationException>(() => Options(lossless, 7, modular, false)
            .EncodeRaw(image, output, Limited(8), TestContext.Current.CancellationToken));
        AssertSentinel(output);
    }

    [Fact]
    public void raw_preview_is_included_in_preflight()
    {
        using JxlRawImage image = new(1, 1, [new([JxlRawPlane.FromIntegers(1, 1, [12], 8)], JxlRawColorSpace.Grayscale)]);
        using JxlRawImage preview = new(256, 256,
            [new([JxlRawPlane.FromIntegers(256, 256, new int[256 * 256], 8)], JxlRawColorSpace.Grayscale)]);
        image.Encoding.Preview = preview.Frames[0];
        using MemoryStream output = Sentinel();
        Assert.Throws<InvalidMemoryOperationException>(() => Options(true, 7, false, false)
            .EncodeRaw(image, output, Limited(8), TestContext.Current.CancellationToken));
        AssertSentinel(output);
    }

    [Fact]
    public void retained_custom_matrices_are_budgeted_across_all_raw_frames()
    {
        JxlRawFrame[] frames = new JxlRawFrame[3];
        float[] weights = new float[128 * 128]; Array.Fill(weights, 1f);
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = new([JxlRawPlane.FromIntegers(1, 1, [12], 8)], JxlRawColorSpace.Grayscale);
            frames[i].Metadata.Duration = 1;
            frames[i].Encoding.QuantizationMatrices = [new() { Transform = JxlDctTransform.Dct128, X = weights, Y = weights, B = weights }];
        }
        using JxlRawImage image = new(1, 1, frames);
        using MemoryStream output = Sentinel();
        JxlEncoder encoder = Options(false, 1, false, false);
        Assert.Throws<InvalidMemoryOperationException>(() => encoder.EncodeRaw(image, output, Limited(16), TestContext.Current.CancellationToken));
        AssertSentinel(output);
        using MemoryStream accepted = new();
        encoder.EncodeRaw(image, accepted, Limited(64), TestContext.Current.CancellationToken);
        accepted.Position = 0;
        using Image<Rgb24> decoded = Image.Load<Rgb24>(accepted);
        Assert.Equal(3, decoded.Frames.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void metadata_is_included_in_preflight(bool raw)
    {
        Configuration configuration = Limited(1);
        using MemoryStream output = Sentinel();
        if (raw)
        {
            using JxlRawImage image = new(1, 1, [new([JxlRawPlane.FromIntegers(1, 1, [1], 8)], JxlRawColorSpace.Grayscale)]);
            image.Metadata.GetJxlMetadata().Jumbf = new byte[1 << 20];
            Assert.Throws<InvalidMemoryOperationException>(() => Options(true, 1, false, false)
                .EncodeRaw(image, output, configuration, TestContext.Current.CancellationToken));
        }
        else
        {
            using Image<Rgba32> image = new(configuration, 1, 1);
            image.Metadata.GetJxlMetadata().Jumbf = new byte[1 << 20];
            Assert.Throws<InvalidMemoryOperationException>(() => Options(true, 1, false, false).Encode(image, output));
        }
        AssertSentinel(output);
    }

    [Theory]
    [InlineData(10)] [InlineData(11)]
    public void global_search_is_rejected_or_explicitly_admitted_without_allocating_the_large_frame(int effort)
    {
        JxlImageHeader image = new() { Size = new(8000, 8000), XybEncoded = false, BitDepth = new(16, 0),
            ExtraChannels = [new() { Type = 0, BitDepth = new(16, 0) }] };
        JxlEncoder encoder = Options(true, effort, false, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidMemoryOperationException>(() => JxlEncoderPreflight.Displayed(image, encoder, Limited(4096).MemoryAllocator, 0));
        JxlEncoderPreflight.Displayed(image, encoder, Limited(262144).MemoryAllocator, 0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1 << 20, $"Preflight allocated {allocated} bytes.");
        Assert.Equal(effort, encoder.Effort);
    }

    [Theory]
    [InlineData(1U, 1U)] [InlineData(1U, 3U)] [InlineData(0U, 1U)]
    public void excessive_toc_geometry_rejects_without_allocating_sections(uint shift, uint passes)
    {
        JxlFrameHeader frame = new() { Width = 16777216, Height = 256, GroupSizeShift = shift, PassCount = passes };
        Assert.Throws<ArgumentException>(() => JxlEncoderPreflight.ValidateToc(frame, shift, passes));
    }

    [Fact]
    public void toc_counts_include_single_group_progression_and_use_sixty_four_bit_geometry()
    {
        JxlFrameHeader frame = new() { Width = 1, Height = 1 };
        Assert.Equal(1, JxlEncoderPreflight.ValidateToc(frame, 1, 1));
        Assert.Equal(6, JxlEncoderPreflight.ValidateToc(frame, 1, 3));
        frame.Width = uint.MaxValue; frame.Height = uint.MaxValue;
        Assert.Throws<ArgumentException>(() => JxlEncoderPreflight.ValidateToc(frame, 0, 3));
        frame.Width = 58252U * 256; frame.Height = 1;
        Assert.Equal(65536, JxlEncoderPreflight.ValidateToc(frame, 1, 1));
        frame.Width += 256;
        Assert.Throws<ArgumentException>(() => JxlEncoderPreflight.ValidateToc(frame, 1, 1));
    }

    [Fact]
    public void encoder_workspace_and_nested_children_report_allocation_limits()
    {
        using JxlModularWorkspace parent = JxlModularWorkspace.ForEncoding(new SimpleGcMemoryAllocator(), 100, 80);
        parent.Allocate(2, 5);
        using (JxlModularWorkspace child = parent.CreateTemporary())
        {
            child.Allocate(2, 5);
            Assert.Throws<InvalidMemoryOperationException>(() => parent.Allocate(1, 1));
            Assert.Throws<InvalidMemoryOperationException>(() => child.Allocate(1, 1));
        }
        parent.Allocate(2, 5);
        Assert.Throws<InvalidMemoryOperationException>(() => parent.Allocate(1, 1));
    }

    [Theory]
    [InlineData(8000, 6000, 7)] [InlineData(6000, 4000, 7)] [InlineData(4000, 3000, 8)]
    public void common_photo_sizes_are_admitted_for_lossy_encoding_by_the_default_allocator(int width, int height, int effort)
    {
        // Measured peaks: 48 MP at effort 7 about 1.7 GB; 12 MP at effort 8 about 2.5 GB.
        JxlImageHeader image = new() { Size = new((uint)width, (uint)height), XybEncoded = true };
        JxlEncoderPreflight.Displayed(image, new JxlEncoder { Effort = effort }, Configuration.Default.MemoryAllocator, 0);
    }

    [Fact]
    public void default_decoder_modular_budget_admits_every_canvas_within_max_pixels()
    {
        // VarDCT keeps three 32-bit coefficient planes; allow one extra channel as well.
        JxlDecoder decoder = new();
        Assert.True(decoder.MaxModularBytes >= decoder.MaxPixels * 4 * sizeof(int));
        JxlJpegReconstructionOptions reconstruction = new();
        Assert.True(reconstruction.MaxModularBytes >= reconstruction.MaxPixels * 3 * sizeof(int));
    }

    [Fact]
    public void raising_the_aggregate_limit_does_not_bypass_contiguous_row_limits()
    {
        const int width = (1 << 26) + 1;
        JxlImageHeader image = new() { Size = new(width, 1), XybEncoded = true };
        JxlFrameHeader frame = new() { Width = width, Height = 1, Upsampling = 8 };
        JxlEncoder encoder = new() { Resampling = 8 };
        Assert.True(JxlEncoderPreflight.ValidateToc(frame, 1, 1) < 65536);
        InvalidMemoryOperationException error = Assert.Throws<InvalidMemoryOperationException>(() =>
            JxlEncoderPreflight.Frame(image, frame, encoder, Limited(262144).MemoryAllocator, width, 1, false, false, 0));
        Assert.Contains("contiguous", error.Message);
    }

    [Fact]
    public void jpeg_transcoding_applies_the_encoder_limit_before_output()
    {
        using Image<Rgb24> source = new(32, 32);
        using MemoryStream jpeg = new(); source.Save(jpeg, new JpegEncoder()); jpeg.Position = 0;
        using MemoryStream output = Sentinel();
        Assert.Throws<InvalidMemoryOperationException>(() => JxlJpegTranscoder.JpegToJxl(Limited(1), jpeg, output,
            new(), TestContext.Current.CancellationToken));
        AssertSentinel(output);
    }

    [Fact]
    public void jpeg_input_growth_honors_the_allocator_even_with_a_larger_input_limit()
    {
        using MemoryStream input = new(new byte[1 << 20], false), output = Sentinel();
        Assert.Throws<InvalidMemoryOperationException>(() => JxlJpegTranscoder.JpegToJxl(Limited(1), input, output,
            new() { MaxInputBytes = 2 << 20 }, TestContext.Current.CancellationToken));
        AssertSentinel(output);
    }

    [Fact]
    public void oversized_jpeg_frame_header_rejects_before_coefficient_allocation()
    {
        using Image<Rgb24> source = new(32, 32);
        using MemoryStream encoded = new(); source.Save(encoded, new JpegEncoder());
        byte[] bytes = encoded.ToArray(); bool changed = false;
        for (int i = 0; i < bytes.Length - 9; i++)
        {
            if (bytes[i] != 255 || bytes[i + 1] != 0xc0) { continue; }
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i + 5), 8000);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i + 7), 8000);
            changed = true; break;
        }
        Assert.True(changed);
        Configuration configuration = Limited(4096);
        using MemoryStream input = new(bytes, false), output = Sentinel();
        long before = GC.GetAllocatedBytesForCurrentThread();
        // An 8000x8000 frame needs at least one coded bit per block; a few hundred
        // bytes cannot describe it, so it is rejected before its coefficient planes.
        InvalidImageContentException error = Assert.Throws<InvalidImageContentException>(() => JxlJpegTranscoder.JpegToJxl(configuration, input,
            output, new(), TestContext.Current.CancellationToken));
        Assert.Contains("too short", error.Message);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1 << 20);
        AssertSentinel(output);
    }

    [Fact]
    public void jpeg_working_storage_estimate_scales_with_coefficient_content()
    {
        // A smooth 2048x2048 JPEG has few AC tokens: coefficient planes, input and
        // tokens fit well inside 128 MiB, although its padded area is 4 Mpixel.
        using Image<Rgb24> source = new(2048, 2048);
        source.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgb24> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++) { row[x] = new((byte)(x >> 3), (byte)(y >> 3), 90); }
            }
        });
        using MemoryStream jpeg = new(); source.Save(jpeg, new JpegEncoder()); jpeg.Position = 0;
        using MemoryStream encoded = new(), restored = new();
        JxlJpegTranscoder.JpegToJxl(Limited(128), jpeg, encoded, new(), TestContext.Current.CancellationToken);
        encoded.Position = 0;
        JxlJpegTranscoder.JxlToJpeg(encoded, restored, TestContext.Current.CancellationToken);
        Assert.Equal(jpeg.ToArray(), restored.ToArray());
    }

    [Fact]
    public void jpeg_token_storage_is_admitted_before_output()
    {
        // Full-entropy 4:4:4 noise at quality 100 retains about one AC token per
        // coefficient. The coefficient planes pass the frame-header check at
        // 64 MiB; the token total then exceeds it before any output is written.
        Random random = new(4711);
        using Image<Rgb24> source = new(1024, 1024);
        source.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgb24> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++) { row[x] = new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)); }
            }
        });
        using MemoryStream jpeg = new();
        source.Save(jpeg, new JpegEncoder { Quality = 100, ColorType = JpegColorType.YCbCrRatio444 });
        byte[] bytes = jpeg.ToArray();
        using MemoryStream limitedInput = new(bytes, false), output = Sentinel();
        InvalidMemoryOperationException error = Assert.Throws<InvalidMemoryOperationException>(() => JxlJpegTranscoder.JpegToJxl(Limited(64),
            limitedInput, output, new(), TestContext.Current.CancellationToken));
        Assert.Contains("working storage", error.Message); AssertSentinel(output);

        using MemoryStream input = new(bytes, false), encoded = new(), restored = new();
        JxlJpegTranscoder.JpegToJxl(Limited(256), input, encoded, new(), TestContext.Current.CancellationToken);
        encoded.Position = 0;
        JxlJpegTranscoder.JxlToJpeg(encoded, restored, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, restored.ToArray());
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(17, 9)] [InlineData(32, 32)]
    public void jpeg_visible_pixel_limit_allows_required_mcu_padding(int width, int height)
    {
        using Image<Rgb24> source = new(width, height, new Rgb24(20, 70, 180));
        using MemoryStream jpeg = new(); source.Save(jpeg, new JpegEncoder()); jpeg.Position = 0;
        using MemoryStream encoded = new(), restored = new();
        JxlJpegTranscoder.JpegToJxl(jpeg, encoded, new() { MaxPixels = (long)width * height }, TestContext.Current.CancellationToken);
        encoded.Position = 0;
        JxlJpegTranscoder.JxlToJpeg(encoded, restored, TestContext.Current.CancellationToken);
        Assert.Equal(jpeg.ToArray(), restored.ToArray());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void admitted_small_images_still_round_trip_with_an_explicit_limit(bool lossless)
    {
        using Image<Rgba32> source = new(Limited(16), 7, 5, new Rgba32(25, 70, 120, 190));
        using MemoryStream output = new(); Options(lossless, 1, false, false).Encode(source, output); output.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(output);
        Assert.Equal((source.Width, source.Height), (decoded.Width, decoded.Height)); Assert.Equal(source[0, 0].A, decoded[0, 0].A);
        if (lossless) { Assert.Equal(source[0, 0], decoded[0, 0]); }
    }

    private static Configuration Limited(int megabytes)
    {
        Configuration result = Configuration.Default.Clone();
        result.MemoryAllocator = MemoryAllocator.Create(new() { AllocationLimitMegabytes = megabytes, MaximumPoolSizeMegabytes = 0 });
        return result;
    }

    private static JxlEncoder Options(bool lossless, int effort, bool modular, bool progressive) => new()
    {
        Lossless = lossless, Effort = effort, AllowExpertEffort = true, UseContainer = true,
        Modular = modular ? true : null, Progressive = progressive ? JxlProgressiveMode.Full : JxlProgressiveMode.None,
    };

    private static MemoryStream Sentinel() { MemoryStream result = new(); result.Write([17, 29, 43]); return result; }
    private static void AssertSentinel(MemoryStream stream) { Assert.Equal(3, stream.Position); Assert.Equal(new byte[] { 17, 29, 43 }, stream.ToArray()); }
}
