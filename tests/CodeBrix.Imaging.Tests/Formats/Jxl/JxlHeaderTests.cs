// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlHeaderTests
{
    internal static string ResourceRoot => Path.Combine(AppContext.BaseDirectory, "SampleFiles", "JxlConformance");

    public static IEnumerable<object[]> RepositoryCases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ResourceRoot, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.EnumerateArray())
        {
            if (item.GetProperty("external").GetBoolean()) { continue; }
            yield return [item.GetProperty("case").GetString(), item.GetProperty("base").GetString(),
                item.GetProperty("width").GetInt32(), item.GetProperty("height").GetInt32(),
                item.GetProperty("bitsPerSample").GetInt32(), item.GetProperty("exponentBitsPerSample").GetInt32(),
                item.GetProperty("inputSha256").GetString()];
        }
    }

    [Theory]
    [MemberData(nameof(RepositoryCases))]
    public void identifies_conformance_dimensions_and_precision(string name, string baseName, int width, int height, int bits, int exponent, string sha256)
    {
        //Arrange
        byte[] data = File.ReadAllBytes(Path.Combine(ResourceRoot, baseName, "input.jxl"));
        Assert.True(File.Exists(Path.Combine(ResourceRoot, name, "test.json")));
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(data)));

        //Act
        IImageInfo info = Image.Identify(data);
        JxlMetadata metadata = info.Metadata.GetJxlMetadata();
        using JxlTestStream forwardOnly = new(data, 251);
        IImageInfo streamed = new JxlDecoder().Identify(Configuration.Default, forwardOnly, TestContext.Current.CancellationToken);
        Assert.False(forwardOnly.IsDisposed);
        Assert.Equal(info.Width, streamed.Width);
        Assert.Equal(info.Height, streamed.Height);
        Assert.Equal(metadata.FrameCount, streamed.Metadata.GetJxlMetadata().FrameCount);

        //Assert: dimensions come from upstream preview images (djxl for grayscale_jpeg).
        Assert.Same(JxlFormat.Instance, info.Format);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal(bits, metadata.BitsPerSample);
        Assert.Equal(exponent, metadata.ExponentBitsPerSample);
        using JsonDocument description = JsonDocument.Parse(File.ReadAllText(Path.Combine(ResourceRoot, name, "test.json")));
        Assert.Equal(description.RootElement.GetProperty("frames").GetArrayLength(), metadata.FrameCount);
        if (info.Metadata.IccProfile != null)
        {
            using MemoryStream headerInput = new(data);
            using JxlCodestreamStream headerStream = new(headerInput, 64 << 20, true, default);
            JxlImageHeader header = JxlImageHeader.Read(new JxlBitReader(headerStream), 100_000_000, 64 << 20);
            // The rendered conformance output can use a different profile (e.g. XYB).
            using JsonDocument originalProfiles = JsonDocument.Parse(File.ReadAllText(Path.Combine(ResourceRoot, "original-icc-sha256.json")));
            if (header.Color.WantIcc)
            {
                JsonElement original = originalProfiles.RootElement.GetProperty("sha256").GetProperty(baseName);
                Assert.Equal(original.GetString(), Convert.ToHexStringLower(SHA256.HashData(info.Metadata.IccProfile.ToByteArray())));
            }
            else
            {
                // Enumerated encodings acquire an ICC profile describing their original color space.
                Assert.NotNull(CodeBrix.Imaging.ColorSpaces.Icc.IccColorProfile.Read(info.Metadata.IccProfile.ToByteArray()));
            }
        }
    }

    [Theory]
    [MemberData(nameof(RepositoryCases))]
    public void shared_header_writer_preserves_conformance_fields(string name, string baseName, int width, int height, int bits, int exponent, string sha256)
    {
        //Arrange
        using FileStream file = File.OpenRead(Path.Combine(ResourceRoot, baseName, "input.jxl"));
        using JxlCodestreamStream container = new(file, 64 * 1024 * 1024, false, default);
        JxlImageHeader original = JxlImageHeader.Read(new JxlBitReader(container), 100_000_000, 64 * 1024 * 1024);
        using MemoryStream encoded = new();
        JxlBitWriter writer = new(encoded);

        //Act
        original.Write(writer);
        writer.ZeroPadToByte();
        encoded.Position = 0;
        JxlImageHeader decoded = JxlImageHeader.Read(new JxlBitReader(encoded), 100_000_000, 64 * 1024 * 1024);

        //Assert
        Assert.True(File.Exists(Path.Combine(ResourceRoot, name, "test.json")));
        Assert.Equal((uint)width, original.DisplayWidth);
        Assert.Equal((uint)height, original.DisplayHeight);
        Assert.Equal((uint)bits, original.BitDepth.Bits);
        Assert.Equal((uint)exponent, original.BitDepth.ExponentBits);
        file.Position = 0;
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(file)));
        Assert.Equal(original.Size, decoded.Size);
        Assert.Equal(original.Orientation, decoded.Orientation);
        Assert.Equal(original.BitDepth, decoded.BitDepth);
        Assert.Equal(original.XybEncoded, decoded.XybEncoded);
        Assert.Equal(original.Color.WantIcc, decoded.Color.WantIcc);
        Assert.Equal(original.Color.ColorSpace, decoded.Color.ColorSpace);
        Assert.Equal(original.IntensityTarget, decoded.IntensityTarget);
        Assert.Equal(original.ExtraChannels.Length, decoded.ExtraChannels.Length);
        Assert.Equal(original.TpsNumerator, decoded.TpsNumerator);
        Assert.Equal(original.TpsDenominator, decoded.TpsDenominator);
        Assert.Equal(original.Up2, decoded.Up2);
        Assert.Equal(original.Up4, decoded.Up4);
        Assert.Equal(original.Up8, decoded.Up8);
        Assert.Equal(original.Opsin, decoded.Opsin);
        for (int i = 0; i < original.ExtraChannels.Length; i++)
        {
            Assert.Equal(original.ExtraChannels[i].Type, decoded.ExtraChannels[i].Type);
            Assert.Equal(original.ExtraChannels[i].BitDepth, decoded.ExtraChannels[i].BitDepth);
            Assert.Equal(original.ExtraChannels[i].Name, decoded.ExtraChannels[i].Name);
        }
    }

    [Fact]
    public void resource_budget_is_at_most_one_hundred_megabytes()
    {
        long bytes = Directory.EnumerateDirectories(Path.GetDirectoryName(ResourceRoot), "Jxl*")
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)).Sum(path => new FileInfo(path).Length);
        Assert.InRange(bytes, 1, 100_000_000);
        Assert.False(File.Exists(Path.Combine(ResourceRoot, "bike", "input.jxl")));
        Assert.False(File.Exists(Path.Combine(ResourceRoot, "cafe", "input.jxl")));
    }

    [Fact]
    public void oversized_header_is_rejected_before_pixel_allocation()
    {
        //Arrange: a valid header for an image larger than the configured limit.
        using MemoryStream stream = new();
        JxlBitWriter writer = new(stream);
        new JxlImageHeader { Size = new(1000, 1000) }.Write(writer);
        writer.ZeroPadToByte();
        stream.Position = 0;

        //Act / Assert
        Assert.Throws<InvalidImageContentException>(() => new JxlDecoder { MaxPixels = 999_999 }.Identify(Configuration.Default, stream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void detector_accepts_both_signatures_and_rejects_short_or_wrong_headers()
    {
        JxlImageFormatDetector detector = new();
        Assert.Same(JxlFormat.Instance, detector.DetectFormat([255, 10]));
        Assert.Same(JxlFormat.Instance, detector.DetectFormat(JxlConstants.ContainerSignature));
        Assert.Null(detector.DetectFormat([255]));
        Assert.Null(detector.DetectFormat([255, 216]));
        Assert.Null(detector.DetectFormat(JxlConstants.ContainerSignature[..11]));
    }
}
