// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Metadata.Profiles.Exif;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlNormalizedEncoderTests
{
    [Theory]
    [InlineData(0)] [InlineData(9)] [InlineData(65535)]
    public void raw_encoder_uses_orientation_fallback_for_invalid_exif(ushort orientation)
    {
        using JxlRawImage source = new(3, 2, [new([JxlRawPlane.FromIntegers(3, 2, new int[6], 8, 0),
            JxlRawPlane.FromIntegers(3, 2, new int[6], 8, 1), JxlRawPlane.FromIntegers(3, 2, new int[6], 8, 2)])]);
        source.Metadata.ExifProfile = new(); source.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
        source.Metadata.GetJxlMetadata().Orientation = 6;
        using MemoryStream encoded = new(); new JxlEncoder { Lossless = true, Effort = 1 }.EncodeRaw(source, encoded, cancellationToken: TestContext.Current.CancellationToken);
        encoded.Position = 0; using Image<Rgb24> decoded = Image.Load<Rgb24>(encoded);
        Assert.Equal(2, decoded.Width); Assert.Equal(3, decoded.Height);
        Assert.Equal(orientation, source.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value);
    }

    [Theory]
    [InlineData(0, 1, 1)] [InlineData(0, 6, 6)] [InlineData(9, 1, 1)]
    [InlineData(65535, 1, 1)] [InlineData(6, 1, 6)] [InlineData(1, 6, 1)]
    public void invalid_exif_orientation_falls_back_without_modifying_source_metadata(ushort exif, int fallback, int expected)
    {
        using Image<Rgb24> source = new(3, 2);
        source.Metadata.ExifProfile = new(); source.Metadata.ExifProfile.SetValue(ExifTag.Orientation, exif);
        source.Metadata.GetJxlMetadata().Orientation = fallback;
        for (int y = 0; y < 2; y++) { for (int x = 0; x < 3; x++) { source[x, y] = new((byte)(x * 60), (byte)(y * 80), 10); } }
        foreach (bool lossless in new[] { false, true })
        {
            using MemoryStream encoded = new(); new JxlEncoder { Lossless = lossless, Effort = 1 }.Encode(source, encoded);
            encoded.Position = 0;
            JxlDecoder decoder = new() { ApplyOrientation = false };
            using Image<Rgb24> coded = decoder.Decode<Rgb24>(Configuration.Default, encoded, TestContext.Current.CancellationToken);
            Assert.Equal(expected, coded.Metadata.GetJxlMetadata().Orientation);
            encoded.Position = 0; using Image<Rgb24> displayed = Image.Load<Rgb24>(encoded);
            Assert.Equal(expected == 6 ? 2 : 3, displayed.Width); Assert.Equal(expected == 6 ? 3 : 2, displayed.Height);
            if (lossless)
            {
                for (int y = 0; y < 2; y++) { for (int x = 0; x < 3; x++)
                { Assert.Equal(source[x, y], expected == 6 ? displayed[1 - y, x] : displayed[x, y]); } }
            }
            Assert.Equal(exif, source.Metadata.ExifProfile.GetValue(ExifTag.Orientation).Value);
            Assert.Equal(fallback, source.Metadata.GetJxlMetadata().Orientation);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(6)]
    public void signed_normalized_samples_preserve_the_full_integer_range(int orientation)
    {
        using Image<NormalizedByte2> bytes2 = new(256, 256);
        using Image<NormalizedByte4> bytes4 = new(256, 256);
        using Image<NormalizedShort2> shorts2 = new(256, 256);
        using Image<NormalizedShort4> shorts4 = new(256, 256);
        for (int i = 0; i < 65536; i++)
        {
            bytes2[i & 255, i >> 8] = new() { PackedValue = (ushort)i };
            bytes4[i & 255, i >> 8] = new() { PackedValue = (uint)i | ((uint)(65535 - i) << 16) };
            shorts2[i & 255, i >> 8] = new() { PackedValue = (uint)i | ((uint)(65535 - i) << 16) };
            shorts4[i & 255, i >> 8] = new() { PackedValue = (ulong)(uint)i | ((ulong)(uint)(i ^ 0x8000) << 16)
                | ((ulong)(uint)(65535 - i) << 32) | ((ulong)(uint)((i + 113) & 65535) << 48) };
        }
        Verify(bytes2); Verify(bytes4); Verify(shorts2); Verify(shorts4);

        void Verify<TPixel>(Image<TPixel> source) where TPixel : unmanaged, IPixel<TPixel>
        {
            source.Metadata.GetJxlMetadata().Orientation = orientation;
            using MemoryStream stream = new(); new JxlEncoder { Lossless = true, Effort = 1 }.Encode(source, stream);
            stream.Position = 0; using Image<TPixel> decoded = Image.Load<TPixel>(stream);
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                { Assert.Equal(source[x, y], orientation == 1 ? decoded[x, y] : decoded[255 - y, x]); }
            }
        }
    }
}
