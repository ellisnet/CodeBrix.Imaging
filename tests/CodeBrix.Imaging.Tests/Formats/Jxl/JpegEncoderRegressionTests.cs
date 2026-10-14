// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JpegEncoderRegressionTests
{
    [Theory]
    [InlineData(JpegColorType.Luminance, 1)] [InlineData(JpegColorType.Rgb, 1)]
    [InlineData(JpegColorType.YCbCrRatio444, 2)] [InlineData(JpegColorType.YCbCrRatio420, 2)]
    public void jpeg_encoder_writes_only_referenced_nonzero_quantizers(JpegColorType type, int expectedCount)
    {
        using Image<Rgb24> image = new(17, 19);
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++) { image[x, y] = new((byte)(x * 11), (byte)(y * 7), (byte)((x + y) * 5)); }
        using MemoryStream jpeg = new(); new JpegEncoder { ColorType = type, Quality = 87 }.Encode(image, jpeg);
        byte[] bytes = jpeg.ToArray(); HashSet<byte> quantizers = new(), referenced = new();
        for (int offset = 2; offset < bytes.Length;)
        {
            Assert.Equal(255, bytes[offset]); byte marker = bytes[offset + 1];
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2));
            if (marker == 0xda) { break; }
            if (marker == 0xdb)
            {
                for (int table = offset + 4; table < offset + 2 + length; table += 65)
                {
                    Assert.True(quantizers.Add(bytes[table]));
                    foreach (byte value in bytes.AsSpan(table + 1, 64)) { Assert.InRange(value, (byte)1, (byte)255); }
                }
            }
            if (marker == 0xc0)
            { for (int c = 0; c < bytes[offset + 9]; c++) { referenced.Add(bytes[offset + 12 + c * 3]); } }
            offset += length + 2;
        }
        Assert.Equal(expectedCount, quantizers.Count); Assert.True(quantizers.SetEquals(referenced));
        jpeg.Position = 0; using MemoryStream jxl = new();
        JxlJpegTranscoder.JpegToJxl(jpeg, jxl, new() { Effort = 1 }, TestContext.Current.CancellationToken);
        jxl.Position = 0; using MemoryStream reconstructed = new();
        JxlJpegTranscoder.JxlToJpeg(jxl, reconstructed, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(bytes, reconstructed.ToArray());
    }

    [Theory]
    [InlineData(JpegColorType.Cmyk)] [InlineData(JpegColorType.YCbCrRatio422)]
    [InlineData(JpegColorType.YCbCrRatio411)] [InlineData(JpegColorType.YCbCrRatio410)]
    public void unsupported_explicit_color_type_rejects_before_output(JpegColorType type)
    {
        using Image<Rgb24> image = new(2, 2); using MemoryStream output = new();
        Assert.Throws<NotSupportedException>(() => new JpegEncoder { ColorType = type }.Encode(image, output));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void unknown_color_type_rejects_before_output()
    {
        using Image<Rgb24> image = new(2, 2); using MemoryStream output = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegEncoder { ColorType = (JpegColorType)255 }.Encode(image, output));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(JpegColorType.Cmyk)] [InlineData(JpegColorType.YCbCrRatio422)]
    public void unsupported_source_metadata_still_chooses_supported_output(JpegColorType sourceType)
    {
        using Image<Rgb24> image = new(2, 2); image.Metadata.GetJpegMetadata().ColorType = sourceType;
        using MemoryStream output = new(); new JpegEncoder().Encode(image, output);
        output.Position = 0;
        IImageInfo info = new JpegDecoder().Identify(Configuration.Default, output, TestContext.Current.CancellationToken);
        Assert.Equal(JpegColorType.YCbCrRatio420, info.Metadata.GetJpegMetadata().ColorType);
    }
}
