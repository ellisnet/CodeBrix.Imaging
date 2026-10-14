// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System.IO;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jpeg;

public class JpegDecoderCoreTests
{
    // SOI, a JFIF APP0 segment, then EOI: recognized as JPEG, but no frame header.
    private static readonly byte[] NoFrame =
    [
        0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00,
        0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xff, 0xd9,
    ];

    // SOI, JFIF APP0, an 8x8 single-component SOF0, then EOI: a frame header without any scan.
    private static readonly byte[] NoScan =
    [
        0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00,
        0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xff, 0xc0, 0x00, 0x0b, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01, 0x01,
        0x11, 0x00, 0xff, 0xd9,
    ];

    [Fact]
    public void identify_without_a_frame_header_throws_invalid_image_content()
    {
        using MemoryStream stream = new(NoFrame, false);
        InvalidImageContentException error = Assert.Throws<InvalidImageContentException>(() => Image.Identify(stream));
        Assert.Contains("SOFn", error.Message);
    }

    [Fact]
    public void load_without_a_frame_header_throws_invalid_image_content()
    {
        using MemoryStream stream = new(NoFrame, false);
        InvalidImageContentException error = Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(stream));
        Assert.Contains("SOFn", error.Message);
    }

    [Fact]
    public void load_without_a_scan_throws_invalid_image_content()
    {
        using MemoryStream stream = new(NoScan, false);
        InvalidImageContentException error = Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgba32>(stream));
        Assert.Contains("SOS", error.Message);
    }

    [Fact]
    public void identify_with_a_frame_header_but_no_scan_reports_the_frame_size()
    {
        using MemoryStream stream = new(NoScan, false);
        IImageInfo info = Image.Identify(stream);
        Assert.Equal((8, 8), (info.Width, info.Height));
    }
}
