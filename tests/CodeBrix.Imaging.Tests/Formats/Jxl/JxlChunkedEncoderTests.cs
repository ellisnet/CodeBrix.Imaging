// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlChunkedEncoderTests
{
    [Theory]
    [InlineData(1, false)] [InlineData(3, false)] [InlineData(7, false)] [InlineData(9, false)]
    [InlineData(1, true)] [InlineData(7, true)]
    public void chunked_public_encoding_preserves_samples_across_lf_boundaries(int effort, bool vertical)
    {
        using Image<Rgba64> source = CreateImage(vertical);
        using ForwardOutput output = new(); new JxlEncoder { Lossless = true, Effort = effort }.Encode(source, output);
        using JxlTestStream input = new(output.Bytes, 7); using Image<Rgba64> actual = Image.Load<Rgba64>(input);
        Compare(source, actual);
    }

    [Fact]
    public void chunked_import_bounds_uncompressed_planes_and_visits_each_edge_once()
    {
        JxlImageHeader image = new() { Size = new(4103, 17), XybEncoded = false, BitDepth = new(8, 0), ExtraChannels = [] };
        JxlFrameHeader frame = new() { Modular = true, Width = 4103, Height = 17, Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        List<Rectangle> calls = [];
        using MemoryStream output = new(); JxlBitWriter writer = new(output); image.Write(writer);
        JxlModularEncoder.WriteFrameChunked(writer, image, frame, (bounds, planes) =>
        {
            calls.Add(bounds); Assert.Equal(3, planes.Length);
            foreach (var plane in planes)
            {
                Assert.Equal(bounds.Width, plane.Width); Assert.Equal(bounds.Height, plane.Height);
                for (int y = 0; y < bounds.Height; y++)
                { for (int x = 0; x < bounds.Width; x++) { plane.Set(x, y, ((bounds.Left + x) * 13 + y) & 255); } }
            }
        }, TestContext.Current.CancellationToken, 1, Configuration.Default.MemoryAllocator);
        Assert.Equal(new[] { new Rectangle(0, 0, 2048, 17), new Rectangle(2048, 0, 2048, 17), new Rectangle(4096, 0, 7, 17) }, calls);
        output.Position = 0; using Image<Rgb24> actual = Image.Load<Rgb24>(output);
        for (int y = 0; y < actual.Height; y++)
        { for (int x = 0; x < actual.Width; x++) { byte value = (byte)((x * 13 + y) & 255); Assert.Equal(new Rgb24(value, value, value), actual[x, y]); } }
        Assert.False(JxlModularEncoder.ShouldChunk(frame, 10)); Assert.False(JxlModularEncoder.ShouldChunk(frame, 11));
        frame.Width = 2048; Assert.False(JxlModularEncoder.ShouldChunk(frame, 7));
    }

    [Theory]
    [InlineData(1, false)] [InlineData(7, false)] [InlineData(7, true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_preserves_chunked_lossless_samples(int effort, bool vertical)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        using Image<Rgba64> source = CreateImage(vertical);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-chunk-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            using (FileStream stream = File.Create(input)) { new JxlEncoder { Lossless = true, Effort = effort }.Encode(source, stream); }
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> actual = Image.Load<Rgba64>(output); Compare(source, actual);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Image<Rgba64> CreateImage(bool vertical)
    {
        Image<Rgba64> image = new(vertical ? 7 : 4103, vertical ? 4103 : 7);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            { int i = y * image.Width + x; image[x, y] = new((ushort)(i * 137), (ushort)(i * 97), (ushort)(i * 991), i % 19 == 0 ? (ushort)0 : (ushort)(i * 53)); }
        }
        return image;
    }

    private static void Compare(Image<Rgba64> source, Image<Rgba64> actual)
    {
        Assert.Equal((source.Width, source.Height), (actual.Width, actual.Height));
        for (int y = 0; y < source.Height; y++)
        { Assert.True(source.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y)), $"Row {y}"); }
    }

    private sealed class ForwardOutput : Stream
    {
        private readonly MemoryStream inner = new();
        internal byte[] Bytes => this.inner.ToArray();
        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => this.inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => this.inner.Write(buffer);
        public override void WriteByte(byte value) => this.inner.WriteByte(value);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { this.inner.Dispose(); } base.Dispose(disposing); }
    }
}
