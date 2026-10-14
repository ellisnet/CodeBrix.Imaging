// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Container;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlContainerTests
{
    [Fact]
    public void brotli_metadata_spanning_multiple_output_chunks_is_preserved()
    {
        byte[] metadata = new byte[250_003];
        new Random(75621).NextBytes(metadata);
        using MemoryStream file = Start();
        WriteBox(file, "brob", Compress("jumb", metadata));
        WriteBox(file, "jxlc", [255, 10]);
        using JxlTestStream input = new(file.ToArray(), 53);
        using JxlCodestreamStream codestream = new(input, 1 << 20, false, TestContext.Current.CancellationToken);
        codestream.CopyTo(Stream.Null);
        Assert.Equal(metadata, Assert.Single(codestream.Boxes).Data);
    }

    [Fact]
    public void reassembles_partial_codestream_around_compressed_metadata_on_forward_only_stream()
    {
        using MemoryStream file = Start();
        WriteBox(file, "test", [1, 2, 3], true);
        WriteBox(file, "jxlp", [0, 0, 0, 0, 255, 10, 1]);
        WriteBox(file, "brob", Compress("xml ", "<x:xmpmeta/>"u8));
        WriteBox(file, "jxlp", [128, 0, 0, 1, 2, 3, 4]);
        using JxlTestStream input = new(file.ToArray(), 1);
        using (JxlCodestreamStream codestream = new(input, 1000, false, TestContext.Current.CancellationToken))
        {
            using MemoryStream output = new();
            codestream.CopyTo(output);
            Assert.Equal(new byte[] { 255, 10, 1, 2, 3, 4 }, output.ToArray());
            Assert.Collection(codestream.Boxes, box =>
            {
                Assert.Equal("xml ", box.Type);
                Assert.Equal("<x:xmpmeta/>"u8.ToArray(), box.Data);
            });
        }
        Assert.False(input.IsDisposed);
    }

    [Theory]
    [InlineData("out-of-order")]
    [InlineData("missing-last")]
    [InlineData("mixed")]
    [InlineData("truncated")]
    [InlineData("short-box")]
    [InlineData("short-extended-box")]
    [InlineData("forbidden-compression")]
    [InlineData("expansion-limit")]
    public void rejects_invalid_container(string corruption)
    {
        using MemoryStream file = Start();
        switch (corruption)
        {
            case "out-of-order": WriteBox(file, "jxlp", [128, 0, 0, 1, 255, 10]); break;
            case "missing-last": WriteBox(file, "jxlp", [0, 0, 0, 0, 255, 10]); break;
            case "mixed": WriteBox(file, "jxlp", [0, 0, 0, 0, 255, 10]); WriteBox(file, "jxlc", [1]); break;
            case "truncated": WriteBox(file, "jxlc", [255, 10]); file.SetLength(file.Length - 1); break;
            case "short-box": file.Write(new byte[] { 0, 0, 0, 7, 106, 120, 108, 99 }); break;
            case "short-extended-box": file.Write(new byte[] { 0, 0, 0, 1, 106, 120, 108, 99, 0, 0, 0, 0, 0, 0, 0, 15 }); break;
            case "forbidden-compression": WriteBox(file, "brob", Compress("jbrd", [1, 2, 3])); break;
            case "expansion-limit": WriteBox(file, "brob", Compress("xml ", new byte[10000])); break;
        }
        using JxlTestStream input = new(file.ToArray());
        using JxlCodestreamStream codestream = new(input, 128, false, TestContext.Current.CancellationToken);
        Assert.Throws<InvalidImageContentException>(() => codestream.CopyTo(Stream.Null));
    }

    [Fact]
    public void identifies_metadata_after_the_codestream_and_preserves_jpeg_reconstruction_flag()
    {
        using FileStream source = File.OpenRead(Path.Combine(JxlHeaderTests.ResourceRoot, "alpha_triangles", "input.jxl"));
        using JxlCodestreamStream raw = new(source, 1 << 20, true, TestContext.Current.CancellationToken);
        using MemoryStream data = new();
        raw.CopyTo(data);
        using MemoryStream file = Start();
        WriteBox(file, "jxlc", data.ToArray());
        WriteBox(file, "brob", Compress("xml ", "<test/>"u8));
        WriteBox(file, "jumb", [1, 2, 3]);
        WriteBox(file, "jbrd", [0]);
        using JxlTestStream input = new(file.ToArray(), 31);
        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, input, TestContext.Current.CancellationToken);
        Assert.Equal("<test/>"u8.ToArray(), info.Metadata.XmpProfile.Data);
        Assert.Equal(new byte[] { 1, 2, 3 }, info.Metadata.GetJxlMetadata().Jumbf);
        Assert.True(info.Metadata.GetJxlMetadata().HasJpegReconstruction);
    }

    [Fact]
    public void honors_cancellation_before_reading_the_input()
    {
        using MemoryStream empty = new();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new JxlDecoder().Identify(Configuration.Default, empty, cancellation.Token));
    }

    private static MemoryStream Start()
    {
        MemoryStream stream = new();
        stream.Write(JxlConstants.ContainerSignature);
        WriteBox(stream, "ftyp", [106, 120, 108, 32, 0, 0, 0, 0, 106, 120, 108, 32]);
        return stream;
    }

    private static void WriteBox(Stream stream, string type, ReadOnlySpan<byte> data, bool extended = false)
    {
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(header, extended ? 1U : (uint)data.Length + 8);
        System.Text.Encoding.ASCII.GetBytes(type, header[4..8]);
        if (extended) { BinaryPrimitives.WriteUInt64BigEndian(header[8..], (ulong)data.Length + 16); }
        stream.Write(header[..(extended ? 16 : 8)]);
        stream.Write(data);
    }

    private static byte[] Compress(string type, ReadOnlySpan<byte> bytes)
    {
        using MemoryStream stream = new();
        stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
        using (BrotliStream brotli = new(stream, CompressionLevel.SmallestSize, true)) { brotli.Write(bytes); }
        return stream.ToArray();
    }
}
