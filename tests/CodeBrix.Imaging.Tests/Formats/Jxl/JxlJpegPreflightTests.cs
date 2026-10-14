// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlJpegPreflightTests
{
    [Theory]
    [InlineData(2047, 1)] [InlineData(-2047, -1)] [InlineData(2047, 2047)]
    public void unrepresentable_final_dc_rejects_before_output_and_allows_pixel_fallback(int first, int second)
        => AssertUnsupportedAndFallback(DcImage(first, second));

    [Theory]
    [InlineData(2047)] [InlineData(-2047)] [InlineData(0)]
    public void representable_dc_boundaries_reconstruct_byte_exactly(int dc) => AssertRoundTrip(DcImage(dc, 0));

    [Fact]
    public void progressive_negative_dc_is_checked_after_refinement() => AssertRoundTrip(DcImage(-1024, 0, true));

    [Fact]
    public void complete_huffman_code_is_invalid_even_with_pixel_fallback()
    {
        byte[] bytes = DcImage(0, 0, completeHuffman: true);
        foreach (bool fallback in new[] { false, true })
        {
            using MemoryStream input = new(bytes, false); using MemoryStream output = new();
            Assert.Contains("all-ones", Assert.Throws<InvalidImageContentException>(() => JxlJpegTranscoder.JpegToJxl(input, output,
                new JxlJpegTranscodeOptions { AllowPixelFallback = fallback }, TestContext.Current.CancellationToken)).Message);
            Assert.Equal(0, output.Length);
        }
    }

    [Fact]
    public void ordinary_progressive_eob_reset_overflow_uses_pixel_fallback_before_output()
    {
        const int blocks = 256 * 257;
        using MemoryStream jpeg = new(); jpeg.Write([255, 0xd8]);
        byte[] quant = new byte[65]; quant.AsSpan(1).Fill(1); Marker(jpeg, 0xdb, quant);
        Marker(jpeg, 0xc2, [8, 8, 8, 8, 0, 1, 1, 0x11, 0]); // 2048 x 2056.
        byte[] tables = new byte[36]; tables[1] = 1; tables[18] = 0x10; tables[19] = 1;
        Marker(jpeg, 0xc4, tables);
        Marker(jpeg, 0xda, [1, 1, 0, 0, 0, 0]);
        JxlJpegBitWriter bits = new(jpeg); int padding = 0;
        for (int i = 0; i < blocks; i++) { bits.Write(0, 1); }
        bits.Flush(null, ref padding);
        Marker(jpeg, 0xda, [1, 1, 0, 1, 63, 0]); bits = new(jpeg);
        for (int i = 0; i < blocks; i++) { bits.Write(0, 1); }
        bits.Flush(null, ref padding); jpeg.Write([255, 0xd9]);
        AssertUnsupportedAndFallback(jpeg.ToArray());
    }

    [Theory]
    [InlineData(-2047, false)] [InlineData(2047, false)] [InlineData(0, false)] [InlineData(-1024, true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_reconstruction_preserves_dc_boundaries_and_empty_payload(int first, bool refined)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        byte[] original = DcImage(first, 0, refined);
        using MemoryStream input = new(original, false); using MemoryStream encoded = new();
        JxlJpegTranscoder.JpegToJxl(input, encoded, new JxlJpegTranscodeOptions { Effort = 1 }, TestContext.Current.CancellationToken);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-jpeg-boundary-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "image.jxl"), restored = Path.Combine(directory, "restored.jpg");
            await File.WriteAllBytesAsync(source, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [source, restored, "--reconstruct_jpeg", "--num_threads=1"], TestContext.Current.CancellationToken);
            Assert.Equal(original, await File.ReadAllBytesAsync(restored, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void oversized_trailer_rejects_before_output_and_allows_pixel_fallback()
    {
        byte[] original = Read("metadata");
        byte[] withTrailer = new byte[original.Length + 65793 + (1 << 22)];
        original.CopyTo(withTrailer, 0);
        AssertUnsupportedAndFallback(withTrailer);
    }

    [Theory]
    [InlineData("unused-zero-quant")] [InlineData("five-quant-tables")] [InlineData("ninety-huffman-tables")]
    public void unsupported_table_layouts_allow_pixel_fallback(string variation)
    {
        byte[] original = Read("gray"); using MemoryStream altered = new(); altered.Write(original.AsSpan(0, 2));
        if (variation == "ninety-huffman-tables")
        { for (int i = 0; i < 90; i++) { Marker(altered, 0xc4, []); } }
        else
        {
            byte[] quant = new byte[65]; quant[0] = 3;
            if (variation == "five-quant-tables") { quant.AsSpan(1).Fill(1); }
            for (int i = 0; i < (variation == "five-quant-tables" ? 4 : 1); i++) { Marker(altered, 0xdb, quant); }
        }
        altered.Write(original.AsSpan(2)); AssertUnsupportedAndFallback(altered.ToArray());
    }

    [Theory]
    [InlineData("reset-count")] [InlineData("zero-count")] [InlineData("block-index")] [InlineData("zero-value")]
    public void reconstruction_field_overflows_are_checked_before_container_output(string variation)
    {
        using JxlJpegParser jpeg = Parse("metadata"); JxlJpegData.Scan scan = jpeg.Data.Scans[0];
        scan.ResetPoints.Clear(); scan.ExtraZeroRuns.Clear();
        if (variation == "reset-count") { for (uint i = 0; i < 65556; i++) { scan.ResetPoints.Add(i); } }
        else if (variation == "zero-count") { for (uint i = 0; i < 65556; i++) { scan.ExtraZeroRuns.Add(i, 1); } }
        else if (variation == "block-index") { scan.ResetPoints.Add((3U << 26) + 1); }
        else { scan.ExtraZeroRuns.Add(0, 276); }
        using MemoryStream output = new();
        Assert.Throws<NotSupportedException>(() => JxlJpegEncoder.Write(output, jpeg, Configuration.Default.MemoryAllocator, 1, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(2, false)] [InlineData(2, true)] [InlineData(3, false)] [InlineData(3, true)]
    public void every_typed_metadata_marker_must_match_its_payload(int type, bool duplicate)
    {
        using JxlJpegParser jpeg = Parse("metadata");
        int index = Array.FindIndex(jpeg.Data.AppMarkers, m => m.Type == type); Assert.True(index >= 0);
        JxlJpegData.AppMarker invalid = new(type, type == 2 ? 9 : 32);
        if (duplicate)
        {
            jpeg.Data.AppMarkers = [.. jpeg.Data.AppMarkers, invalid];
            jpeg.Data.Markers = [.. jpeg.Data.Markers.AsSpan(0, jpeg.Data.Markers.Length - 1), 0xe1, 0xd9];
        }
        else { jpeg.Data.AppMarkers[index] = invalid; }
        using MemoryStream output = new();
        Assert.Contains("metadata lengths", Assert.Throws<InvalidImageContentException>(() => JxlJpegReconstructor.Write(
            output, jpeg.Data, jpeg.Coefficients, jpeg.Icc, jpeg.Exif, jpeg.Xmp, TestContext.Current.CancellationToken)).Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void zero_length_icc_markers_cannot_silently_discard_a_profile()
    {
        using JxlJpegParser jpeg = Parse("split-icc"); Assert.NotEmpty(jpeg.Icc);
        for (int i = 0; i < jpeg.Data.AppMarkers.Length; i++)
        { if (jpeg.Data.AppMarkers[i].Type == 1) { jpeg.Data.AppMarkers[i] = new(1, 17); } }
        using MemoryStream output = new();
        Assert.Contains("ICC length", Assert.Throws<InvalidImageContentException>(() => JxlJpegReconstructor.Write(
            output, jpeg.Data, jpeg.Coefficients, jpeg.Icc, jpeg.Exif, jpeg.Xmp, TestContext.Current.CancellationToken)).Message);
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(65537)] [InlineData(-65537)] [InlineData(40000)] [InlineData(-40000)]
    public void reconstruction_rejects_ac_values_instead_of_wrapping_to_sixteen_bits(int value)
    {
        using JxlJpegParser jpeg = Parse("baseline-444");
        jpeg.Coefficients.Planes[jpeg.Coefficients.ComponentPlane(0)].Set(1, 0, value);
        using MemoryStream output = new();
        Assert.Contains("16-bit", Assert.Throws<InvalidImageContentException>(() => JxlJpegReconstructor.Write(
            output, jpeg.Data, jpeg.Coefficients, jpeg.Icc, jpeg.Exif, jpeg.Xmp, TestContext.Current.CancellationToken)).Message);
    }

    private static void AssertUnsupportedAndFallback(byte[] bytes)
    {
        using MemoryStream input = new(bytes, false); using MemoryStream output = new();
        Assert.Throws<NotSupportedException>(() => JxlJpegTranscoder.JpegToJxl(input, output, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length); input.Position = 0;
        JxlJpegTranscoder.JpegToJxl(input, output, new JxlJpegTranscodeOptions { AllowPixelFallback = true, Effort = 1 }, TestContext.Current.CancellationToken);
        using Image<Rgba32> expected = Image.Load<Rgba32>(bytes); output.Position = 0;
        using Image<Rgba32> actual = Image.Load<Rgba32>(output);
        Assert.Equal(expected.Size(), actual.Size()); Assert.False(actual.Metadata.GetJxlMetadata().HasJpegReconstruction);
        for (int y = 0; y < expected.Height; y++)
        { Assert.True(expected.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
    }

    private static void AssertRoundTrip(byte[] bytes)
    {
        using MemoryStream input = new(bytes, false); using MemoryStream encoded = new(); using MemoryStream restored = new();
        JxlJpegTranscoder.JpegToJxl(input, encoded, new JxlJpegTranscodeOptions { Effort = 1 }, TestContext.Current.CancellationToken);
        encoded.Position = 0; JxlJpegTranscoder.JxlToJpeg(encoded, restored, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, restored.ToArray());
    }

    private static JxlJpegParser Parse(string name) => JxlJpegParser.Read(Read(name), Configuration.Default.MemoryAllocator, 100_000_000, TestContext.Current.CancellationToken);
    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Jpeg", name + ".jpg"));

    // Original tiny JPEG construction: 8-bit gray, unit quantizers, a four-bit
    // DC code for each category 0..11, and a one-bit AC EOB code.
    private static byte[] DcImage(int first, int second, bool refined = false, bool completeHuffman = false)
    {
        using MemoryStream output = new(); output.Write([255, 0xd8]);
        byte[] quant = new byte[65]; quant.AsSpan(1).Fill(1); Marker(output, 0xdb, quant);
        Marker(output, refined ? 0xc2 : 0xc0, [8, 0, 8, 0, (byte)(refined ? 8 : 16), 1, 1, 0x11, 0]);
        int symbols = completeHuffman ? 2 : 12, dcLength = 17 + symbols;
        byte[] tables = new byte[dcLength + 18]; tables[completeHuffman ? 1 : 4] = (byte)symbols;
        for (int i = 0; i < symbols; i++) { tables[17 + i] = (byte)i; }
        tables[dcLength] = 0x10; tables[dcLength + 1] = 1;
        Marker(output, 0xc4, tables);
        Marker(output, 0xda, [1, 1, 0, 0, (byte)(refined ? 0 : 63), (byte)(refined ? 1 : 0)]);
        JxlJpegBitWriter bits = new(output); int padding = 0;
        Dc(first); if (!refined) { Dc(second); } bits.Flush(null, ref padding);
        if (refined)
        {
            Marker(output, 0xda, [1, 1, 0, 0, 0, 0x10]);
            bits = new(output); bits.Write(1, 1); bits.Flush(null, ref padding);
        }
        output.Write([255, 0xd9]); return output.ToArray();
        void Dc(int value)
        {
            int category = value == 0 ? 0 : BitOperations.Log2((uint)Math.Abs(value)) + 1;
            bits.Write((uint)category, completeHuffman ? 1 : 4); bits.Write(unchecked((ulong)(value < 0 ? value - 1 : value)), category);
            if (!refined) { bits.Write(0, 1); }
        }
    }

    private static void Marker(Stream stream, int marker, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[4]; header[0] = 255; header[1] = (byte)marker;
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], checked((ushort)(payload.Length + 2)));
        stream.Write(header); stream.Write(payload);
    }
}
