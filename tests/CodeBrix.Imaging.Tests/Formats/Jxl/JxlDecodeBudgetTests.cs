// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlDecodeBudgetTests
{
    private const int CanvasPixels = 32 * 16;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void retained_canvas_budget_counts_full_cropped_frames_and_honors_display_limit(bool raw)
    {
        byte[] bytes = CroppedAnimation(3, false);
        JxlDecoder decoder = new() { MaxTotalPixels = 2 * CanvasPixels };
        InvalidImageContentException error = Assert.Throws<InvalidImageContentException>(() => Decode(bytes, decoder, raw));
        Assert.Contains("MaxTotalPixels", error.Message);
        decoder.MaxFrames = 2;
        Assert.Equal(2, Decode(bytes, decoder, raw));
        decoder.MaxFrames = 3; decoder.MaxTotalPixels = 3 * CanvasPixels;
        Assert.Equal(3, Decode(bytes, decoder, raw));
    }

    [Fact]
    public void uncoalesced_raw_budget_counts_retained_layers_including_hidden_frames()
    {
        byte[] bytes = CroppedAnimation(3, true);
        JxlDecoder decoder = new() { Coalesce = false, MaxTotalPixels = 2 };
        Assert.Contains("MaxTotalPixels", Assert.Throws<InvalidImageContentException>(() => Decode(bytes, decoder, true)).Message);
        decoder.MaxTotalPixels = 3;
        Assert.Equal(3, Decode(bytes, decoder, true));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void hidden_cropped_frames_consume_canvas_work_before_a_display_frame(bool raw)
    {
        byte[] bytes = CroppedAnimation(3, true);
        JxlDecoder decoder = new() { MaxDecodedPixels = 2 * CanvasPixels, MaxFrames = 1 };
        Assert.Contains("MaxDecodedPixels", Assert.Throws<InvalidImageContentException>(() => Decode(bytes, decoder, raw)).Message);
        decoder.MaxDecodedPixels = 3 * CanvasPixels;
        Assert.Equal(1, Decode(bytes, decoder, raw));
    }

    [Theory]
    [InlineData("identify")] [InlineData("pixels")] [InlineData("raw")] [InlineData("stream")]
    public void coded_frame_limit_counts_hidden_frames_on_every_read_api(string operation)
    {
        byte[] bytes = CroppedAnimation(3, true);
        JxlDecoder decoder = new() { MaxCodedFrames = 2, MaxFrames = 1 };
        Assert.Contains("MaxCodedFrames", Assert.Throws<InvalidImageContentException>(() => Read()).Message);
        decoder.MaxCodedFrames = 3;
        Read();
        void Read()
        {
            if (operation == "pixels" || operation == "raw") { Decode(bytes, decoder, operation == "raw"); return; }
            using JxlTestStream stream = new(bytes);
            if (operation == "identify") { decoder.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken); return; }
            using JxlAnimationReader reader = JxlAnimationReader.Open(stream, new() { Decoder = decoder, Loop = false, DecodeAhead = 1 }, TestContext.Current.CancellationToken);
            while (reader.TryGetNextFrame(out JxlAnimationFrame frame)) { frame.Dispose(); }
        }
    }

    [Fact]
    public void streaming_uses_work_budget_instead_of_accumulating_retained_canvas_pixels()
    {
        byte[] bytes = CroppedAnimation(3, false);
        JxlDecoder decoder = new() { MaxTotalPixels = 1, MaxDecodedPixels = 3 * CanvasPixels };
        Assert.Equal(3, Read());
        decoder.MaxDecodedPixels--;
        Assert.Contains("MaxDecodedPixels", Assert.Throws<InvalidImageContentException>(() => Read()).Message);
        int Read()
        {
            using JxlTestStream stream = new(bytes);
            using JxlAnimationReader reader = JxlAnimationReader.Open(stream, new() { Decoder = decoder, Loop = false, DecodeAhead = 1 }, TestContext.Current.CancellationToken);
            int count = 0;
            while (reader.TryGetNextFrame(out JxlAnimationFrame frame)) { frame.Dispose(); count++; }
            return count;
        }
    }

    [Fact]
    public void identify_does_not_charge_pixels_it_never_decodes_or_retains()
    {
        using JxlTestStream stream = new(CroppedAnimation(3, false));
        JxlDecoder decoder = new() { MaxTotalPixels = 1, MaxDecodedPixels = 1, MaxCodedFrames = 3 };
        Assert.Equal(3, decoder.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken).Metadata.GetJxlMetadata().FrameCount);
    }

    [Theory]
    [InlineData("total")] [InlineData("work")] [InlineData("frames")] [InlineData("memory")]
    public void nonpositive_budgets_are_rejected_before_reading(string option)
    {
        JxlDecoder decoder = new();
        if (option == "total") { decoder.MaxTotalPixels = 0; }
        else if (option == "work") { decoder.MaxDecodedPixels = -1; }
        else if (option == "memory") { decoder.MaxModularBytes = 0; }
        else { decoder.MaxCodedFrames = 0; }
        using MemoryStream input = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Decode<Rgba32>(Configuration.Default, input, TestContext.Current.CancellationToken));
    }

    private static int Decode(byte[] bytes, JxlDecoder decoder, bool raw)
    {
        using JxlTestStream stream = new(bytes);
        if (raw)
        {
            using JxlRawImage image = decoder.DecodeRaw(Configuration.Default, stream, TestContext.Current.CancellationToken);
            return image.Frames.Count;
        }
        using Image<Rgba32> pixels = decoder.Decode<Rgba32>(Configuration.Default, stream, TestContext.Current.CancellationToken);
        return pixels.Frames.Count;
    }

    private static byte[] CroppedAnimation(int count, bool hidden)
    {
        using Image<L8> source = new(1, 1, new L8(127));
        using MemoryStream original = new();
        new JxlEncoder { Lossless = true, Effort = 1, UseContainer = false }.Encode(source, original);
        original.Position = 0;
        JxlBitReader reader = new(original, TestContext.Current.CancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(reader, 100, 1 << 20);
        JxlFrameHeader frame = JxlFrameHeader.Read(reader, header, 100, 1 << 20);
        JxlFrameToc toc = JxlFrameToc.Read(reader, frame, Configuration.Default.MemoryAllocator);
        byte[] payload = original.ToArray().AsSpan((int)(reader.Position / 8), (int)toc.TotalBytes).ToArray();
        header.Size = new(32, 16); header.IsAnimated = true; header.LoopCount = 1;
        frame.HaveCrop = true;
        using MemoryStream output = new(); JxlBitWriter writer = new(output);
        header.Write(writer);
        for (int i = 0; i < count; i++)
        {
            frame.IsLast = i == count - 1;
            frame.Duration = hidden && !frame.IsLast ? 0U : 1U;
            frame.Write(writer, header); JxlFrameToc.WriteOrdered(writer, frame, toc.Sizes);
            writer.WriteBytes(payload);
        }
        return output.ToArray();
    }
}
