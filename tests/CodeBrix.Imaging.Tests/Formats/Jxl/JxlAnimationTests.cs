// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;
using Xunit;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlAnimationTests
{
    [Theory]
    [InlineData("animation_icos4d", JxlPixelFormat.Bgra32)]
    [InlineData("animation_spline", JxlPixelFormat.Bgra32)]
    [InlineData("animation_newtons_cradle", JxlPixelFormat.Rgba32)]
    public async Task streamed_canvases_match_image_decode(string name, JxlPixelFormat format)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(JxlHeaderTests.ResourceRoot, name, "input.jxl"));
        using MemoryStream baseline = new(bytes, false);
        JxlDecoder decoder = new() { MaxFrames = 4, TargetColor = JxlTargetColor.Srgb };
        using Image<Rgba32> expected = decoder.Decode<Rgba32>(Configuration.Default, baseline, TestContext.Current.CancellationToken);
        using JxlTestStream input = new(bytes, 113);
        await using JxlAnimationReader reader = JxlAnimationReader.Open(input, new() { Decoder = decoder, Loop = false, DecodeAhead = 1, PixelFormat = format }, TestContext.Current.CancellationToken);
        int index = 0;
        await foreach (JxlAnimationFrame frame in reader.WithCancellation(TestContext.Current.CancellationToken))
        {
            using (frame)
            {
                Assert.Equal(expected.Width, frame.Width); Assert.Equal(expected.Height, frame.Height);
                Assert.Equal(expected.Frames[index].Metadata.GetJxlMetadata().Duration, frame.Metadata.Duration);
                ReadOnlySpan<byte> pixels = frame.Pixels;
                for (int y = 0, p = 0; y < frame.Height; y++)
                {
                    for (int x = 0; x < frame.Width; x++, p += 4)
                    {
                        Rgba32 pixel = expected.Frames[index][x, y];
                        Assert.Equal(format == JxlPixelFormat.Bgra32 ? pixel.B : pixel.R, pixels[p]);
                        Assert.Equal(pixel.G, pixels[p + 1]);
                        Assert.Equal(format == JxlPixelFormat.Bgra32 ? pixel.R : pixel.B, pixels[p + 2]);
                        Assert.Equal(pixel.A, pixels[p + 3]);
                    }
                }
                index++;
            }
        }
        Assert.Equal(4, index); Assert.False(input.IsDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void finite_loop_count_and_timing_work_for_seekable_and_forward_only_streams(bool forwardOnly)
    {
        byte[] bytes = TinyAnimation(2);
        using Stream input = forwardOnly ? new JxlTestStream(bytes, 1) : new MemoryStream(bytes, false);
        using JxlAnimationReader reader = JxlAnimationReader.Open(input, new() { DecodeAhead = 1 }, TestContext.Current.CancellationToken);
        Assert.True(reader.CanSeek);
        List<TimeSpan> times = [], durations = [];
        int count = 0;
        while (reader.TryGetNextFrame(out JxlAnimationFrame frame))
        {
            using (frame)
            {
                Assert.Equal((uint)(count / 3), frame.LoopIndex);
                Assert.Equal(8, frame.Pixels.Length); Assert.Equal(2, frame.Width); Assert.Equal(1, frame.Height);
                times.Add(frame.Timestamp); durations.Add(frame.Duration); count++;
                Assert.True(count <= 6);
            }
        }
        Assert.Equal(6, count);
        Assert.Equal(new[] { 0, 100, 300, 0, 100, 300 }, times.ConvertAll(t => (int)t.TotalMilliseconds));
        Assert.Equal(new[] { 100, 200, 300, 100, 200, 300 }, durations.ConvertAll(t => (int)t.TotalMilliseconds));
        Assert.Equal(3, reader.Metadata.GetJxlMetadata().FrameCount);
    }

    [Fact]
    public void seeking_replays_dependencies_and_discards_queued_frames()
    {
        using MemoryStream input = new(TinyAnimation(1), false);
        using JxlAnimationReader reader = JxlAnimationReader.Open(input, new() { Loop = false, DecodeAhead = 1 }, TestContext.Current.CancellationToken);
        Assert.True(reader.TryGetNextFrame(out JxlAnimationFrame first)); first.Dispose();
        reader.SeekToKeyframe(TimeSpan.FromMilliseconds(150));
        Assert.True(reader.TryGetNextFrame(out JxlAnimationFrame second));
        using (second) { Assert.Equal(TimeSpan.FromMilliseconds(100), second.Timestamp); Assert.Equal("frame-1", second.Metadata.Name); }
        Assert.True(reader.TryGetNextFrame(out JxlAnimationFrame third)); third.Dispose();
        Assert.False(reader.TryGetNextFrame(out _));
        reader.SeekToKeyframe(TimeSpan.Zero);
        Assert.True(reader.TryGetNextFrame(out JxlAnimationFrame again));
        using (again) { Assert.Equal("frame-0", again.Metadata.Name); }
    }

    [Fact]
    public async Task cancellation_stops_a_full_queue_without_invalidating_delivered_frames()
    {
        using JxlTestStream input = new(TinyAnimation(0));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        JxlAnimationReader reader = JxlAnimationReader.Open(input, new() { DecodeAhead = 1 }, cancellation.Token);
        Assert.True(reader.TryGetNextFrame(out JxlAnimationFrame retained));
        using (retained)
        {
            cancellation.Cancel();
            await using (IAsyncEnumerator<JxlAnimationFrame> frames = reader.GetAsyncEnumerator(TestContext.Current.CancellationToken))
            { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => frames.MoveNextAsync().AsTask()); }
            await reader.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(8, retained.Pixels.Length);
        }
        Assert.Throws<ObjectDisposedException>(() => retained.PixelMemory);
        Assert.False(input.IsDisposed);
    }

    [Fact]
    public void replay_cache_limit_and_stream_ownership_are_enforced()
    {
        using JxlTestStream input = new(TinyAnimation(0));
        using JxlAnimationReader reader = JxlAnimationReader.Open(input, new() { MaxBufferedInputBytes = 64, DecodeAhead = 1, LeaveOpen = false }, TestContext.Current.CancellationToken);
        Assert.Throws<InvalidImageContentException>(() =>
        {
            while (reader.TryGetNextFrame(out JxlAnimationFrame frame)) { frame.Dispose(); }
        });
        reader.Dispose(); Assert.True(input.IsDisposed);
    }

    [Fact]
    public void malformed_animation_header_is_reported_when_opening()
    {
        using JxlTestStream input = new([0xff, 0x0a]);
        Assert.Throws<InvalidImageContentException>(() => JxlAnimationReader.Open(input, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(input.IsDisposed);
    }

    [Fact]
    public void repeated_playback_keeps_frame_buffers_bounded_and_releases_all_allocations()
    {
        TrackingAllocator allocator = new();
        Configuration configuration = Configuration.Default.Clone(); configuration.MemoryAllocator = allocator;
        using MemoryStream input = new(TinyAnimation(0), false);
        using (JxlAnimationReader reader = JxlAnimationReader.Open(configuration, input, new() { DecodeAhead = 2 }, TestContext.Current.CancellationToken))
        {
            for (int i = 0; i < 120; i++)
            {
                Assert.True(reader.TryGetNextFrame(out JxlAnimationFrame frame));
                using (frame) { Assert.Equal(8, frame.Pixels.Length); }
            }
        }
        // Two queued frames, one in progress, and one held by the consumer.
        Assert.InRange(allocator.PeakFrames, 1, 4);
        Assert.Equal(0, allocator.Outstanding);
    }

    private sealed class TrackingAllocator : MemoryAllocator
    {
        private int frames;
        internal int Outstanding, PeakFrames;
        protected internal override int GetBufferCapacityInBytes() => 1 << 20;

        public override IMemoryOwner<T> Allocate<T>(int length, AllocationOptions options = AllocationOptions.None)
        {
            Interlocked.Increment(ref this.Outstanding);
            bool frame = typeof(T) == typeof(byte) && length == 8;
            if (frame)
            {
                int count = Interlocked.Increment(ref this.frames);
                this.PeakFrames = Math.Max(this.PeakFrames, count);
            }
            return new Owner<T>(this, new T[length], frame);
        }

        private sealed class Owner<T>(TrackingAllocator allocator, T[] array, bool frame) : IMemoryOwner<T>
        {
            private T[] buffer = array;
            public Memory<T> Memory => this.buffer ?? throw new ObjectDisposedException(nameof(Owner<T>));
            public void Dispose()
            {
                if (Interlocked.Exchange(ref this.buffer, null) == null) { return; }
                Interlocked.Decrement(ref allocator.Outstanding);
                if (frame) { Interlocked.Decrement(ref allocator.frames); }
            }
        }
    }

    internal static byte[] TinyAnimation(uint loops)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "integer31.jxl"));
        using MemoryStream input = new(original, false);
        JxlBitReader reader = new(input, default);
        JxlImageHeader header = JxlImageHeader.Read(reader, 100, 1 << 20);
        JxlFrameHeader frame = JxlFrameHeader.Read(reader, header, 100, 1 << 20);
        JxlFrameToc toc = JxlFrameToc.Read(reader, frame, Configuration.Default.MemoryAllocator);
        byte[] payload = original.AsSpan((int)(reader.Position / 8), (int)toc.TotalBytes).ToArray();
        header.IsAnimated = true; header.LoopCount = loops;
        using MemoryStream output = new(); JxlBitWriter writer = new(output);
        header.Write(writer);
        for (uint i = 0; i < 3; i++)
        {
            writer.ZeroPadToByte(); writer.WriteBool(false); writer.WriteBits(2, 0); writer.WriteBool(true);
            writer.WriteU64(0); writer.WriteBool(false); writer.WriteBits(2, 0);
            writer.WriteBits(2, 1); writer.WriteBits(2, 0); writer.WriteBool(false);
            writer.WriteBits(2, 0); writer.WriteU32((i + 1) * 10, 0, 1, U(8), U(32)); writer.WriteBool(i == 2);
            if (i != 2) { writer.WriteBits(2, 0); }
            JxlName.Write(writer, "frame-" + i);
            writer.WriteBool(false); writer.WriteBool(false); writer.WriteBits(2, 0); writer.WriteU64(0); writer.WriteU64(0);
            writer.WriteBool(false); writer.ZeroPadToByte();
            writer.WriteU32((uint)payload.Length, U(10), U(14, 1024), U(22, 17408), U(30, 4211712));
            writer.ZeroPadToByte(); output.Write(payload);
        }
        return output.ToArray();
    }
}
