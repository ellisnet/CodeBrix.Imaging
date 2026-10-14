// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlJpegReconstructionTests
{
    private static string SyntheticRoot => Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Jpeg");

    public static IEnumerable<object[]> OfflineCases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(SyntheticRoot, "manifest.json")));
        foreach (JsonElement test in manifest.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [test.GetProperty("name").GetString(), test.GetProperty("inputSha256").GetString(),
                test.GetProperty("referenceSha256").GetString()];
        }
    }

    [Theory]
    [MemberData(nameof(OfflineCases))]
    public void offline_jpeg_reconstruction_is_byte_exact_and_forward_only(string name, string inputHash, string referenceHash)
    {
        byte[] encoded = File.ReadAllBytes(Path.Combine(SyntheticRoot, name + ".jxl"));
        byte[] expected = File.ReadAllBytes(Path.Combine(SyntheticRoot, name + ".jpg"));
        Assert.Equal(inputHash, Convert.ToHexStringLower(SHA256.HashData(encoded)));
        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(expected)));
        using JxlTestStream input = new(encoded, chunkSize: 3);
        using MemoryStream output = new();
        JxlJpegTranscoder.JxlToJpeg(input, output, TestContext.Current.CancellationToken);
        Assert.False(input.IsDisposed); Assert.True(output.CanWrite);
        Assert.Equal(expected, output.ToArray());
    }

    [Theory]
    [MemberData(nameof(OfflineCases))]
    public void rewriting_reconstruction_data_preserves_original_jpeg_bytes(string name, string inputHash, string referenceHash)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(SyntheticRoot, name + ".jxl"));
        Assert.Equal(inputHash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using MemoryStream source = new(bytes, false);
        using JxlCodestreamStream original = new(source, 64 << 20, false, TestContext.Current.CancellationToken);
        using MemoryStream codestream = new(); original.CopyTo(codestream);
        using MemoryStream rewritten = new(); JxlContainerWriter.Start(rewritten, original.Level, TestContext.Current.CancellationToken);
        bool found = false;
        foreach ((string type, byte[] box) in original.Boxes)
        {
            byte[] payload = box;
            if (type == "jbrd")
            {
                JxlJpegData data = JxlJpegData.Read(box, 64 << 20, TestContext.Current.CancellationToken);
                payload = data.Write(7, TestContext.Current.CancellationToken); found = true;
            }
            JxlContainerWriter.WriteBox(rewritten, Encoding.ASCII.GetBytes(type), payload, TestContext.Current.CancellationToken);
        }
        Assert.True(found);
        JxlContainerWriter.WriteBox(rewritten, "jxlc"u8, codestream.ToArray(), TestContext.Current.CancellationToken); rewritten.Position = 0;
        using MemoryStream jpeg = new(); JxlJpegTranscoder.JxlToJpeg(rewritten, jpeg, TestContext.Current.CancellationToken);
        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(jpeg.ToArray())));
    }

    [Theory]
    [MemberData(nameof(OfflineCases))]
    public void managed_jpeg_parser_and_reconstruction_writer_preserve_every_original_byte(string name, string inputHash, string referenceHash)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(SyntheticRoot, name + ".jpg"));
        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(original)));
        Assert.NotEmpty(inputHash);
        using JxlJpegParser parsed = JxlJpegParser.Read(original, Configuration.Default.MemoryAllocator, 100_000_000, TestContext.Current.CancellationToken);
        byte[] box = parsed.Data.Write(7, TestContext.Current.CancellationToken);
        JxlJpegData decoded = JxlJpegData.Read(box, 64 << 20, TestContext.Current.CancellationToken);
        using MemoryStream output = new();
        JxlJpegReconstructor.Write(output, decoded, parsed.Coefficients, parsed.Icc, parsed.Exif, parsed.Xmp, TestContext.Current.CancellationToken);
        Assert.Equal(original, output.ToArray());
    }

    [Theory]
    [MemberData(nameof(OfflineCases))]
    public void managed_jpeg_coefficient_codestream_round_trip_is_byte_exact(string name, string inputHash, string referenceHash)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(SyntheticRoot, name + ".jpg"));
        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(original))); Assert.NotEmpty(inputHash);
        using JxlTestStream source = new(original, chunkSize: 3);
        using MemoryStream jxl = new();
        JxlJpegTranscoder.JpegToJxl(source, jxl, TestContext.Current.CancellationToken); jxl.Position = 0;
        Assert.False(source.IsDisposed); Assert.True(jxl.CanWrite);
        using MemoryStream jpeg = new(); JxlJpegTranscoder.JxlToJpeg(jxl, jpeg, TestContext.Current.CancellationToken);
        Assert.Equal(original, jpeg.ToArray());
    }

    [Theory]
    [MemberData(nameof(OfflineCases))]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_djxl_reconstructs_managed_jpeg_transcode_and_agrees_on_pixels(string name, string inputHash, string referenceHash)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        byte[] original = File.ReadAllBytes(Path.Combine(SyntheticRoot, name + ".jpg"));
        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(original))); Assert.NotEmpty(inputHash);
        using MemoryStream encoded = new();
        using MemoryStream jpegSource = new(original, false);
        JxlJpegTranscoder.JpegToJxl(jpegSource, encoded, TestContext.Current.CancellationToken);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-jpeg-encode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), jpeg = Path.Combine(directory, "restored.jpg"), png = Path.Combine(directory, "decoded.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            string log = await JxlNativeReference.RunAsync("djxl", [input, jpeg, "--reconstruct_jpeg", "--num_threads=1"], TestContext.Current.CancellationToken);
            byte[] reconstructed = await File.ReadAllBytesAsync(jpeg, TestContext.Current.CancellationToken);
            Assert.True(original.AsSpan().SequenceEqual(reconstructed), log);
            await JxlNativeReference.RunAsync("djxl", [input, png, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> native = Image.Load<Rgba64>(png);
            encoded.Position = 0; using Image<Rgba64> managed = Image.Load<Rgba64>(encoded);
            Assert.Equal(native.Size(), managed.Size());
            for (int y = 0; y < native.Height; y++)
            {
                for (int x = 0; x < native.Width; x++)
                {
                    Rgba64 a = native[x, y], b = managed[x, y];
                    Assert.True(Math.Abs(a.R - b.R) <= 3 && Math.Abs(a.G - b.G) <= 3 && Math.Abs(a.B - b.B) <= 3,
                        $"{name}: pixel {x},{y}: native={a}, managed={b}");
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void jpeg_reconstruction_honors_cancellation_before_reading_or_writing()
    {
        using MemoryStream input = new([0xff, 0x0a]);
        using MemoryStream output = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => JxlJpegTranscoder.JxlToJpeg(input, output, cancellation.Token));
        Assert.Equal(0, input.Position); Assert.Equal(0, output.Length);
        Assert.True(input.CanRead); Assert.True(output.CanWrite);
    }

    [Fact]
    public void jpeg_reconstruction_rejects_incomplete_container_without_closing_streams()
    {
        byte[] inputBytes = File.ReadAllBytes(Path.Combine(SyntheticRoot, "metadata.jxl"));
        using JxlTestStream input = new(inputBytes[..^1]);
        using MemoryStream output = new();
        Assert.Throws<InvalidImageContentException>(() => JxlJpegTranscoder.JxlToJpeg(input, output, TestContext.Current.CancellationToken));
        Assert.False(input.IsDisposed); Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData("bench_oriented_brg")]
    [InlineData("grayscale_jpeg")]
    [InlineData("cafe")]
    [Trait("Category", "JxlConformance")]
    public async Task reconstructed_jpeg_matches_original_bytes(string name)
    { await VerifyJpegAsync(name); }

    internal static async Task VerifyJpegAsync(string name)
    {
        JxlExternalResources.RequireEnabled();
        JxlReferenceFixture fixture = new();
        string reference = await fixture.GetAsync(name, "reconstructed.jpg", TestContext.Current.CancellationToken);
        string source = await fixture.GetInputAsync(name, TestContext.Current.CancellationToken);
        using FileStream input = File.OpenRead(source);
        using MemoryStream output = new();
        JxlJpegTranscoder.JxlToJpeg(input, output, TestContext.Current.CancellationToken);
        Assert.True(input.CanRead); Assert.True(output.CanWrite);
        byte[] expected = await File.ReadAllBytesAsync(reference, TestContext.Current.CancellationToken);
        byte[] actual = output.ToArray();
        int firstDifference = 0;
        while (firstDifference < Math.Min(expected.Length, actual.Length) && expected[firstDifference] == actual[firstDifference]) { firstDifference++; }
        Assert.True(expected.AsSpan().SequenceEqual(actual),
            $"{name}: original={expected.Length} bytes, reconstructed={actual.Length}, first difference={firstDifference}");
    }

    [Theory]
    [InlineData("bench_oriented_brg", false)]
    [InlineData("grayscale_jpeg", true)]
    public void reconstruction_metadata_preserves_jpeg_structure(string name, bool gray)
    {
        byte[] box = ReadReconstructionBox(name);
        JxlJpegData data = JxlJpegData.Read(box, 64 << 20, TestContext.Current.CancellationToken);
        Assert.Equal(gray, data.IsGray);
        Assert.Equal(0xd9, data.Markers[^1]);
        Assert.NotEmpty(data.Scans);
        Assert.Equal(gray ? 1 : 3, data.Components.Length);
        Assert.NotEmpty(data.QuantTables);
        Assert.NotEmpty(data.HuffmanCodes);
        Assert.Equal(data.Data.Length, data.AppLength + data.CommentLength + data.IntermarkerLength + data.TailLength);
    }

    [Theory]
    [InlineData("bench_oriented_brg")]
    [InlineData("grayscale_jpeg")]
    public void incomplete_reconstruction_payload_is_rejected(string name)
    {
        byte[] box = ReadReconstructionBox(name);
        Assert.Throws<InvalidImageContentException>(() => JxlJpegData.Read(box[..^1], 64 << 20, TestContext.Current.CancellationToken));
        Assert.Throws<InvalidImageContentException>(() => JxlJpegData.Read(box, 8, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("bench_oriented_brg")]
    [InlineData("grayscale_jpeg")]
    public void jpeg_coefficients_decode_without_a_pixel_render(string name)
    {
        using FileStream input = File.OpenRead(Path.Combine(JxlHeaderTests.ResourceRoot, name, "input.jxl"));
        using JxlCodestreamStream stream = new(input, 64 << 20, false, TestContext.Current.CancellationToken);
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        JxlImageHeader image = JxlImageHeader.Read(reader, 100_000_000, 64 << 20);
        if (image.Color.WantIcc) { JxlIccCodec.ReadEncoded(reader, 64 << 20, Configuration.Default.MemoryAllocator); }
        JxlFrameHeader header = JxlFrameHeader.Read(reader, image, 100_000_000, 64 << 20);
        JxlFrameToc toc = JxlFrameToc.Read(reader, header, Configuration.Default.MemoryAllocator);
        using JxlModularWorkspace workspace = new(Configuration.Default.MemoryAllocator, 100_000_000);
        using JxlVarDctFrame frame = JxlVarDctFrame.Read(reader, image, header, toc, workspace, TestContext.Current.CancellationToken, jpegReconstruction: true);
        Assert.NotNull(frame.JpegCoefficients);
        Assert.Null(frame.TakeFrame());
        Assert.Null(frame.Sigma);
        Assert.All(frame.JpegCoefficients.Quantization, table => Assert.Equal(64, table.Length));
    }

    [Fact]
    public void jpeg_entropy_bytes_use_msb_order_and_stuff_every_ff()
    {
        using MemoryStream output = new();
        JxlJpegBitWriter writer = new(output);
        writer.Write(5, 3);
        writer.Write(255, 8);
        int padding = 0;
        writer.Flush(null, ref padding);
        Assert.Equal(new byte[] { 0xbf, 0xff, 0 }, output.ToArray());
        output.SetLength(0);
        for (int i = 0; i < 600; i++) { writer.Write(ulong.MaxValue, 64); }
        writer.Flush(null, ref padding);
        byte[] bytes = output.ToArray();
        Assert.Equal(9600, bytes.Length);
        for (int i = 0; i < bytes.Length; i += 2) { Assert.Equal(255, bytes[i]); Assert.Equal(0, bytes[i + 1]); }
    }

    [Fact]
    public void jpeg_huffman_sentinel_reserves_the_all_ones_code()
    {
        JxlJpegData.HuffmanCode definition = new() { Counts = new byte[17], Values = [0, 1, 0] };
        definition.Counts[1] = 1; definition.Counts[2] = 2;
        JxlJpegHuffman table = JxlJpegHuffman.Build(definition);
        using MemoryStream output = new();
        JxlJpegBitWriter writer = new(output);
        table.Write(writer, 1); table.Write(writer, 0);
        int padding = 0;
        writer.Flush(null, ref padding);
        Assert.Equal(new byte[] { 0x9f }, output.ToArray());
        Assert.Throws<InvalidImageContentException>(() => table.Write(writer, 2));
        definition.Counts[1] = 3;
        Assert.Throws<InvalidImageContentException>(() => JxlJpegHuffman.Build(definition));
    }

    private static byte[] ReadReconstructionBox(string name)
    {
        using FileStream input = File.OpenRead(Path.Combine(JxlHeaderTests.ResourceRoot, name, "input.jxl"));
        using JxlCodestreamStream stream = new(input, 64 << 20, false, TestContext.Current.CancellationToken);
        stream.Drain();
        Assert.True(stream.HasJpegReconstruction);
        return Assert.Single(stream.Boxes, box => box.Type == "jbrd").Data;
    }
}
