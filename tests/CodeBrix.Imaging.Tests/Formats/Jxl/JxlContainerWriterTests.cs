// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlContainerWriterTests
{
    [Fact]
    public void partial_codestreams_and_extended_metadata_survive_forward_only_io()
    {
        byte[] bytes = Encode(); using JxlTestStream input = new(bytes, 1);
        using Image<Rgba32> decoded = Image.Load<Rgba32>(input);
        Verify(decoded); Assert.Equal("<x:xmpmeta>parts</x:xmpmeta>"u8.ToArray(), decoded.Metadata.XmpProfile.Data);
        Assert.Equal(new byte[] { 9, 8, 7 }, decoded.Metadata.GetJxlMetadata().Jumbf);
    }

    [Fact]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_reads_partial_codestreams_and_extended_boxes()
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-box-writer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, Encode(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=8", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba32> decoded = Image.Load<Rgba32>(output); Verify(decoded);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void large_lengths_are_64_bit_and_overflow_or_truncated_payloads_fail()
    {
        using MemoryStream output = new();
        JxlContainerWriter.WriteBoxHeader(output, "jumb"u8, 1UL << 40, false, TestContext.Current.CancellationToken);
        Assert.Equal(16, output.Length); byte[] header = output.ToArray();
        Assert.Equal(1U, BinaryPrimitives.ReadUInt32BigEndian(header)); Assert.Equal((1UL << 40) + 16, BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8)));
        output.SetLength(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => JxlContainerWriter.WriteBoxHeader(output, "jumb"u8, ulong.MaxValue, false, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => JxlContainerWriter.WriteCodestreamPart(output, 1U << 31, true, [], TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
        using JxlTestStream truncated = new([1, 2], 1);
        Assert.Throws<EndOfStreamException>(() => JxlContainerWriter.WriteBox(output, "jumb"u8, truncated, 3, true, TestContext.Current.CancellationToken));
        Assert.False(truncated.IsDisposed);
    }

    private static byte[] Encode()
    {
        using Image<Rgba32> source = new(19, 7);
        for (int y = 0; y < source.Height; y++) { for (int x = 0; x < source.Width; x++) { source[x, y] = Pixel(x, y); } }
        using MemoryStream encoded = new(); new JxlEncoder { Lossless = true, Effort = 1, UseContainer = false }.Encode(source, encoded);
        byte[] data = encoded.ToArray(); using MemoryStream container = new();
        JxlContainerWriter.Start(container, 5, TestContext.Current.CancellationToken);
        byte[] xml = "<x:xmpmeta>parts</x:xmpmeta>"u8.ToArray();
        using JxlTestStream metadata = new(xml, 2);
        JxlContainerWriter.WriteBox(container, "xml "u8, metadata, (ulong)xml.Length, true, TestContext.Current.CancellationToken);
        for (int offset = 0, index = 0; offset < data.Length; index++)
        {
            int count = Math.Min(11, data.Length - offset);
            JxlContainerWriter.WriteCodestreamPart(container, (uint)index, offset + count == data.Length, data.AsSpan(offset, count), TestContext.Current.CancellationToken);
            if (index == 0) { JxlContainerWriter.WriteMetadata(container, "jumb"u8, [9, 8, 7], false, 1, TestContext.Current.CancellationToken); }
            offset += count;
        }
        return container.ToArray();
    }

    private static Rgba32 Pixel(int x, int y) => new((byte)(x * 13), (byte)(y * 37), (byte)(x * y * 17), x % 3 == 0 ? (byte)0 : (byte)255);
    private static void Verify(Image<Rgba32> image)
    {
        for (int y = 0; y < 7; y++) { for (int x = 0; x < 19; x++) { Assert.Equal(Pixel(x, y), image[x, y]); } }
    }
}
