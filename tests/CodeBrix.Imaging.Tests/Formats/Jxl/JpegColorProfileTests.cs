// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using CodeBrix.Imaging.ColorSpaces.Icc;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JpegColorProfileTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void cmyk_and_ycck_decode_to_srgb_then_save_as_jxl(bool ycck, bool profileAfterFrame)
    {
        byte[] profile = ReadCmykProfile();
        byte[] samples = ycck ? [64, 128, 128, 230] : [204, 153, 102, 230];
        byte[] jpeg = ConstantJpeg(samples, ycck, profile, profileAfterFrame);
        using MemoryStream input = new(jpeg, false);
        IImageInfo info = new JpegDecoder().Identify(Configuration.Default, input, TestContext.Current.CancellationToken);
        Assert.Equal(profile, info.Metadata.IccProfile.ToByteArray()); input.Position = 0;
        using Image<Rgb24> decoded = Image.Load<Rgb24>(input);
        Assert.Null(decoded.Metadata.IccProfile);
        float[] cmyk = ycck ? [64f / 255, 64f / 255, 64f / 255, 25f / 255]
            : [51f / 255, 102f / 255, 153f / 255, 25f / 255];
        Span<float> rgb = stackalloc float[3]; IccColorProfile.Srgb.FromXyz(IccColorProfile.Read(profile).ToXyz(cmyk), rgb);
        Rgb24 expected = default; expected.FromScaledVector4(new(rgb[0], rgb[1], rgb[2], 1));
        for (int y = 0; y < decoded.Height; y++)
        for (int x = 0; x < decoded.Width; x++)
        {
            Rgb24 actual = decoded[x, y];
            Assert.InRange(Math.Abs(expected.R - actual.R), 0, 1);
            Assert.InRange(Math.Abs(expected.G - actual.G), 0, 1);
            Assert.InRange(Math.Abs(expected.B - actual.B), 0, 1);
        }
        using MemoryStream jxl = new(); new JxlEncoder { Lossless = true, Effort = 1 }.Encode(decoded, jxl);
        jxl.Position = 0; using Image<Rgb24> roundTrip = Image.Load<Rgb24>(jxl);
        for (int y = 0; y < decoded.Height; y++)
        for (int x = 0; x < decoded.Width; x++) { Assert.Equal(decoded[x, y], roundTrip[x, y]); }
    }

    [Fact]
    public void ignored_metadata_does_not_enable_cmyk_icc_conversion()
    {
        byte[] jpeg = ConstantJpeg([204, 153, 102, 230], false, ReadCmykProfile(), false);
        using MemoryStream stream = new(jpeg, false);
        using Image<Rgb24> image = new JpegDecoder { IgnoreMetadata = true }.Decode<Rgb24>(Configuration.Default, stream, TestContext.Current.CancellationToken);
        Assert.Null(image.Metadata.IccProfile);
        Assert.Equal(new Rgb24(184, 138, 92), image[0, 0]);
    }

    private static byte[] ReadCmykProfile()
    {
        using FileStream source = File.OpenRead(Path.Combine(JxlHeaderTests.ResourceRoot, "cmyk_layers", "input.jxl"));
        return new JxlDecoder().Identify(Configuration.Default, source, TestContext.Current.CancellationToken).Metadata.IccProfile.ToByteArray();
    }

    private static byte[] ConstantJpeg(byte[] samples, bool ycck, byte[] profile, bool profileAfterFrame)
    {
        using MemoryStream jpeg = new(); jpeg.Write([255, 0xd8]);
        Marker(jpeg, 0xee, [65, 100, 111, 98, 101, 0, 100, 0, 0, 0, 0, (byte)(ycck ? 2 : 0)]);
        if (!profileAfterFrame) { Profile(); }
        byte[] quant = new byte[65]; quant.AsSpan(1).Fill(1); Marker(jpeg, 0xdb, quant);
        Marker(jpeg, 0xc0, [8, 0, 19, 0, 17, 4, 1, 0x11, 0, 2, 0x11, 0, 3, 0x11, 0, 4, 0x11, 0]);
        if (profileAfterFrame) { Profile(); }
        byte[] tables = new byte[47]; tables[4] = 12;
        for (int i = 0; i < 12; i++) { tables[17 + i] = (byte)i; }
        tables[29] = 0x10; tables[30] = 1; Marker(jpeg, 0xc4, tables);
        Marker(jpeg, 0xda, [4, 1, 0, 2, 0, 3, 0, 4, 0, 0, 63, 0]);
        JxlJpegBitWriter writer = new(jpeg);
        for (int block = 0; block < 9; block++)
        foreach (byte sample in samples)
        {
            int dc = block == 0 ? 8 * (sample - 128) : 0, magnitude = Math.Abs(dc), category = 0;
            while (magnitude != 0) { category++; magnitude >>= 1; }
            writer.Write((uint)category, 4);
            if (category != 0) { writer.Write((uint)(dc < 0 ? dc + (1 << category) - 1 : dc), category); }
            writer.Write(0, 1);
        }
        int padding = 0; writer.Flush(null, ref padding); jpeg.Write([255, 0xd9]); return jpeg.ToArray();

        void Profile()
        {
            const int capacity = 65519;
            int count = (profile.Length + capacity - 1) / capacity;
            for (int i = 0; i < count; i++)
            {
                int length = Math.Min(capacity, profile.Length - i * capacity); byte[] part = new byte[14 + length];
                "ICC_PROFILE\0"u8.CopyTo(part); part[12] = (byte)(i + 1); part[13] = (byte)count;
                profile.AsSpan(i * capacity, length).CopyTo(part.AsSpan(14)); Marker(jpeg, 0xe2, part);
            }
        }
    }

    private static void Marker(Stream stream, byte marker, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[4]; header[0] = 255; header[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)(data.Length + 2)); stream.Write(header); stream.Write(data);
    }
}
