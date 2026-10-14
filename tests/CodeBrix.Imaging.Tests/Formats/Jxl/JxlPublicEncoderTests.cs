// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Metadata.Profiles.Exif;
using CodeBrix.Imaging.Metadata.Profiles.Icc;
using CodeBrix.Imaging.Metadata.Profiles.Xmp;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlPublicEncoderTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void public_lossless_encoder_preserves_common_integer_pixel_formats(int effort)
    {
        Verify<L8>(effort); Verify<L16>(effort); Verify<La16>(effort); Verify<La32>(effort);
        Verify<Rgb24>(effort); Verify<Bgr24>(effort); Verify<Rgb48>(effort); Verify<Rgba64>(effort);
        Verify<Rgba32>(effort); Verify<Bgra32>(effort); Verify<Argb32>(effort); Verify<Abgr32>(effort); Verify<Byte4>(effort);
        Verify<A8>(effort); Verify<Bgr565>(effort); Verify<Bgra4444>(effort); Verify<Bgra5551>(effort); Verify<Rgba1010102>(effort);
        Verify<Rg32>(effort); Verify<Short2>(effort); Verify<Short4>(effort);
        Verify<NormalizedByte2>(effort); Verify<NormalizedByte4>(effort); Verify<NormalizedShort2>(effort); Verify<NormalizedShort4>(effort);

        static void Verify<TPixel>(int effort) where TPixel : unmanaged, IPixel<TPixel>
        {
            const int width = 19, height = 13;
            TPixel[] samples = new TPixel[width * height]; new Random(24176).NextBytes(MemoryMarshal.AsBytes(samples.AsSpan()));
            MemoryMarshal.AsBytes(samples.AsSpan(0, 1)).Clear();
            MemoryMarshal.AsBytes(samples.AsSpan(1, 1)).Fill(255);
            MemoryMarshal.AsBytes(samples.AsSpan(2, 1)).Fill(128);
            using Image<TPixel> image = new(width, height);
            for (int y = 0; y < height; y++) { samples.AsSpan(y * width, width).CopyTo(image.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y)); }
            using MemoryStream encoded = new();
            image.SaveAsJxl(encoded, new JxlEncoder { Lossless = true, Effort = effort, AllowExpertEffort = effort == 11 });
            Assert.True(encoded.CanWrite); encoded.Position = 0;
            using Image<TPixel> actual = Image.Load<TPixel>(encoded);
            TPixel[] output = new TPixel[samples.Length]; actual.CopyPixelDataTo(output);
            Assert.True(MemoryMarshal.AsBytes(samples.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(output.AsSpan())), typeof(TPixel).Name);
        }
    }

    [Fact]
    public void floating_input_preserves_raw_sample_bits_and_transparent_colors()
    {
        int[] patterns = [0, int.MinValue, 1, unchecked((int)0x80000001), 0x3f800000, 0x7f800000,
            unchecked((int)0xff800000), 0x7fc12345, unchecked((int)0xffc54321), 0x7f7fffff];
        using Image<RgbaVector> source = new(patterns.Length, 1);
        for (int i = 0; i < patterns.Length; i++)
        { float value = BitConverter.Int32BitsToSingle(patterns[i]); source[i, 0] = new(value, value, value, i % 2 == 0 ? 0 : 1); }
        using MemoryStream encoded = new(); new JxlEncoder { Lossless = true }.Encode(source, encoded); encoded.Position = 0;
        using JxlRawImage raw = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, encoded, TestContext.Current.CancellationToken);
        for (int c = 0; c < 3; c++)
        {
            ReadOnlySpan<float> row = raw.Frames[0].Planes[c].GetFloatRowSpan(0);
            for (int i = 0; i < row.Length; i++) { Assert.Equal(patterns[i], BitConverter.SingleToInt32Bits(row[i])); }
        }
        Assert.Equal(10, raw.Metadata.GetJxlMetadata().Level);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void metadata_and_displayed_animation_canvases_round_trip(bool compress)
    {
        using Image<Rgba32> source = new(7, 5, new Rgba32(12, 18, 87, 0));
        source.Frames.CreateFrame(new Rgba32(58, 97, 12, 138));
        source.Metadata.IccProfile = new IccProfile(JxlIccSynthesis.Create(new JxlColorEncoding()));
        source.Metadata.ExifProfile = new ExifProfile(); source.Metadata.ExifProfile.SetValue(ExifTag.Software, "CodeBrix JPEG XL metadata round trip");
        source.Metadata.XmpProfile = new XmpProfile("<x:xmpmeta>metadata metadata metadata metadata metadata metadata metadata</x:xmpmeta>"u8.ToArray());
        JxlMetadata metadata = source.Metadata.GetJxlMetadata();
        metadata.Jumbf = new byte[128]; metadata.LoopCount = 3; metadata.TicksPerSecondNumerator = 1000;
        for (int i = 0; i < source.Frames.Count; i++)
        {
            JxlFrameMetadata frame = source.Frames[i].Metadata.GetJxlMetadata();
            frame.Duration = (uint)(17 + i); frame.Timecode = (uint)(120 + i); frame.Name = "display-" + i;
            frame.CropLeft = -10; frame.CropWidth = 3; frame.Blending = new(JxlBlendMode.Add, 0, false, 3);
        }
        using MemoryStream encoded = new();
        new JxlEncoder { Lossless = true, CompressMetadataBoxes = compress }.Encode(source, encoded); encoded.Position = 0;
        using Image<Rgba32> actual = Image.Load<Rgba32>(encoded);
        Assert.Equal(source.Metadata.IccProfile.ToByteArray(), actual.Metadata.IccProfile.ToByteArray());
        Assert.Equal(source.Metadata.XmpProfile.Data, actual.Metadata.XmpProfile.Data);
        Assert.Equal("CodeBrix JPEG XL metadata round trip", actual.Metadata.ExifProfile.GetValue(ExifTag.Software).Value);
        Assert.Equal(metadata.Jumbf, actual.Metadata.GetJxlMetadata().Jumbf);
        Assert.Equal(metadata.LoopCount, actual.Metadata.GetJxlMetadata().LoopCount);
        Assert.Equal(2, actual.Frames.Count);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(source.Frames[i].Metadata.GetJxlMetadata().Duration, actual.Frames[i].Metadata.GetJxlMetadata().Duration);
            Assert.Equal(source.Frames[i].Metadata.GetJxlMetadata().Timecode, actual.Frames[i].Metadata.GetJxlMetadata().Timecode);
            Assert.Equal(source.Frames[i].Metadata.GetJxlMetadata().Name, actual.Frames[i].Metadata.GetJxlMetadata().Name);
            for (int y = 0; y < source.Height; y++)
            { Assert.True(source.Frames[i].PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames[i].PixelBuffer.DangerousGetRowSpan(y))); }
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void animated_gif_timing_and_loop_count_carry_into_jpeg_xl(bool lossless)
    {
        using Image<Rgba32> animation = new(16, 16, new Rgba32(255, 0, 0, 255));
        animation.Metadata.GetGifMetadata().RepeatCount = 3;
        animation.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 50;
        for (int i = 0; i < 2; i++)
        {
            using Image<Rgba32> frame = new(16, 16, new Rgba32(0, (byte)(100 * i), 255, 255));
            animation.Frames.AddFrame(frame.Frames.RootFrame).Metadata.GetGifMetadata().FrameDelay = 25;
        }
        using MemoryStream gif = new(); animation.SaveAsGif(gif); gif.Position = 0;
        using Image<Rgba32> loaded = Image.Load<Rgba32>(gif);
        using MemoryStream jxl = new(); loaded.SaveAsJxl(jxl, new JxlEncoder { Lossless = lossless }); jxl.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(jxl);
        JxlMetadata metadata = decoded.Metadata.GetJxlMetadata();
        Assert.Equal((100U, 1U, 3U), (metadata.TicksPerSecondNumerator, metadata.TicksPerSecondDenominator, metadata.LoopCount));
        Assert.Equal([50U, 25U, 25U], new[] { 0, 1, 2 }.Select(i => decoded.Frames[i].Metadata.GetJxlMetadata().Duration));

        // Explicit JPEG XL metadata takes precedence over GIF metadata.
        loaded.Metadata.GetJxlMetadata().LoopCount = 7;
        foreach (ImageFrame<Rgba32> frame in loaded.Frames) { frame.Metadata.GetJxlMetadata().Duration = 9; }
        using MemoryStream explicitJxl = new(); loaded.SaveAsJxl(explicitJxl, new JxlEncoder { Lossless = lossless }); explicitJxl.Position = 0;
        using Image<Rgba32> explicitDecoded = Image.Load<Rgba32>(explicitJxl);
        Assert.Equal(7U, explicitDecoded.Metadata.GetJxlMetadata().LoopCount);
        Assert.All<ImageFrame<Rgba32>>(explicitDecoded.Frames, frame => Assert.Equal(9U, frame.Metadata.GetJxlMetadata().Duration));
    }

    [Fact]
    public async Task async_encoding_uses_forward_only_output_and_keeps_it_open()
    {
        using Image<Rgb24> source = new(11, 4, new Rgb24(78, 91, 18));
        using AsyncOutput output = new();
        await new JxlEncoder { Distance = 0 }.EncodeAsync(source, output, TestContext.Current.CancellationToken);
        Assert.True(output.CanWrite);
        using MemoryStream bytes = new(output.Bytes); using Image<Rgb24> actual = Image.Load<Rgb24>(bytes);
        Assert.Equal(source[0, 0], actual[0, 0]);
        using CancellationTokenSource canceled = new(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JxlEncoder { Lossless = true }.EncodeAsync(source, output, canceled.Token));
    }

    [Fact]
    public void invalid_or_conflicting_options_fail_before_output()
    {
        using Image<Rgb24> source = new(1, 1); using MemoryStream output = new();
        JxlEncoder[] invalid = [new() { Effort = 0 }, new() { Lossless = true, Effort = 11 }, new() { Distance = float.NaN },
            new() { Lossless = true, Resampling = 2 }, new() { Lossless = true, Modular = false }, new() { Lossless = true, PhotonNoiseIso = 1 }];
        foreach (JxlEncoder encoder in invalid) { Assert.ThrowsAny<ArgumentException>(() => encoder.Encode(source, output)); Assert.Equal(0, output.Length); }
        source.Metadata.XmpProfile = new XmpProfile("<x/>"u8.ToArray());
        Assert.ThrowsAny<ArgumentException>(() => new JxlEncoder { Lossless = true, UseContainer = false }.Encode(source, output));
        Assert.Equal(0, output.Length);
    }

    private sealed class AsyncOutput : Stream
    {
        private readonly MemoryStream inner = new();
        internal byte[] Bytes => this.inner.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => this.inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Expected asynchronous writes.");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => this.inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => this.inner.WriteAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing) { if (disposing) { this.inner.Dispose(); } base.Dispose(disposing); }
    }
}
