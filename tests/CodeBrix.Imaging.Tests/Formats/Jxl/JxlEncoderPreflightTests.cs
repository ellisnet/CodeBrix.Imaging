// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlEncoderPreflightTests
{
    private static readonly string[] InvalidNames = [new('a', 1072), new('Ω', 536), "\ud800", "\udc00"];

    [Theory]
    [InlineData(false, false, 0)] [InlineData(false, false, 1)]
    [InlineData(false, true, 0)] [InlineData(false, true, 1)]
    [InlineData(true, false, 0)] [InlineData(true, false, 1)]
    [InlineData(true, true, 0)] [InlineData(true, true, 1)]
    public void all_displayed_frame_names_are_validated_before_output(bool lossless, bool container, int badFrame)
    {
        using Image<Rgb24> source = new(1, 1);
        source.Frames.AddFrame(source.Frames.RootFrame);
        JxlEncoder encoder = new() { Lossless = lossless, Effort = 1, UseContainer = container };
        foreach (string name in InvalidNames)
        {
            source.Frames[badFrame].Metadata.GetJxlMetadata().Name = name;
            using MemoryStream output = new();
            Assert.ThrowsAny<ArgumentException>(() => encoder.Encode(source, output));
            Assert.Equal(0, output.Length);
        }
    }

    [Theory]
    [InlineData(false, false, 0)] [InlineData(false, false, 1)]
    [InlineData(false, true, 0)] [InlineData(false, true, 1)]
    [InlineData(true, false, 0)] [InlineData(true, false, 1)]
    [InlineData(true, true, 0)] [InlineData(true, true, 1)]
    public void all_raw_frame_names_are_validated_before_output(bool lossless, bool container, int badFrame)
    {
        using JxlRawImage source = new(1, 1,
            [new([JxlRawPlane.FromIntegers(1, 1, [127], 8)], JxlRawColorSpace.Grayscale),
             new([JxlRawPlane.FromIntegers(1, 1, [191], 8)], JxlRawColorSpace.Grayscale)]);
        source.Frames[0].Metadata.Duration = 1;
        JxlEncoder encoder = new() { Lossless = lossless, Effort = 1, UseContainer = container };
        foreach (string name in InvalidNames)
        {
            source.Frames[badFrame].Metadata.Name = name;
            using MemoryStream output = new();
            Assert.ThrowsAny<ArgumentException>(() => encoder.EncodeRaw(source, output, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(0, output.Length);
        }
    }

    [Fact]
    public void extra_channel_names_reject_invalid_utf8_at_plane_construction()
    {
        foreach (string name in InvalidNames)
        {
            Assert.ThrowsAny<ArgumentException>(() =>
            {
                using JxlRawPlane plane = JxlRawPlane.FromIntegers(1, 1, [255], 8, extraChannel: new() { Name = name });
            });
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void maximum_length_ascii_and_multibyte_names_round_trip(bool lossless)
    {
        string[] names = [new('a', 1071), new string('Ω', 535) + "a", new string('Ω', 533) + "💠a"];
        foreach (string name in names)
        {
            using Image<Rgb24> source = new(1, 1);
            source.Frames.RootFrame.Metadata.GetJxlMetadata().Name = name;
            using MemoryStream encoded = new();
            new JxlEncoder { Lossless = lossless, Effort = 1 }.Encode(source, encoded);
            encoded.Position = 0;
            using Image<Rgb24> decoded = Image.Load<Rgb24>(encoded);
            Assert.Equal(name, decoded.Frames.RootFrame.Metadata.GetJxlMetadata().Name);
        }
    }
}
