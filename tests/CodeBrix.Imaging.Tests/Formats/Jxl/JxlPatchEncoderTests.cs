// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlPatchEncoderTests
{
    [Theory]
    [InlineData(5)] [InlineData(7)]
    public void repeated_tiles_select_a_smaller_patch_stream_without_changing_pixels(int effort)
    {
        using Image<Rgba32> source = RepeatedTiles(); byte[] encoded = EncodePublic(source, effort);
        using MemoryStream input = new(encoded); using Image<Rgba32> actual = Image.Load<Rgba32>(input);
        for (int y = 0; y < source.Height; y++)
        { Assert.True(source.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
        input.Position = 0; JxlBitReader reader = new(input, TestContext.Current.CancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(reader, 1_000_000, 1_000_000);
        JxlFrameHeader first = JxlFrameHeader.Read(reader, header, 1_000_000, 1_000_000);
        Assert.Equal(2U, first.FrameType); // A reference-only patch atlas was selected.
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace workspace = new(allocator, source.Width * source.Height);
        JxlModularPlane[] planes = new JxlModularPlane[4];
        for (int c = 0; c < 4; c++) { planes[c] = workspace.Allocate(source.Width, source.Height); }
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            { Rgba32 pixel = source[x, y]; planes[0].Set(x, y, pixel.R); planes[1].Set(x, y, pixel.G); planes[2].Set(x, y, pixel.B); planes[3].Set(x, y, pixel.A); }
        }
        JxlFrameHeader plain = new() { Modular = true, Width = (uint)source.Width, Height = (uint)source.Height,
            ExtraUpsampling = [1], ExtraBlending = [default], Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        using MemoryStream baseline = new(); JxlBitWriter writer = new(baseline); header.Write(writer);
        JxlModularEncoder.WriteFrame(writer, header, plain, planes, TestContext.Current.CancellationToken, false, effort, allocator);
        Assert.True(encoded.Length < baseline.Length, $"Patches: {encoded.Length}; plain: {baseline.Length}");
    }

    [Fact]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_automatically_selected_lossless_patches()
    {
        JxlExternalResources.RequireEnabled();
        using Image<Rgba32> source = RepeatedTiles(); await CompareWithNative(EncodePublic(source, 5));
    }

    private static Image<Rgba32> RepeatedTiles()
    {
        Image<Rgba32> image = new(192, 128); Random random = new(573105);
        Rgba32[][] tiles = new Rgba32[8][];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = new Rgba32[64];
            for (int j = 0; j < 64; j++) { tiles[i][j] = new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), j % 7 == 0 ? (byte)0 : (byte)random.Next(256)); }
        }
        for (int top = 0; top < image.Height; top += 8)
        {
            for (int left = 0; left < image.Width; left += 8)
            {
                Rgba32[] tile = tiles[random.Next(tiles.Length)];
                for (int y = 0; y < 8; y++) { tile.AsSpan(y * 8, 8).CopyTo(image.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(top + y).Slice(left, 8)); }
            }
        }
        return image;
    }

    private static byte[] EncodePublic(Image<Rgba32> image, int effort)
    {
        using MemoryStream stream = new(); new JxlEncoder { Lossless = true, Effort = effort }.Encode(image, stream); return stream.ToArray();
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void patch_modes_preserve_reference_slots_extra_channels_and_target_deltas(int mode)
    {
        byte[] bytes = Encode((uint)mode, 32, 1);
        using JxlTestStream stream = new(bytes, 3); using Image<Rgba32> image = Image.Load<Rgba32>(stream);
        Assert.Equal(new Rgba32(20, 30, 40, 64), image[0, 0]);
        Assert.Equal(image[12, 5], image[1, 9]);
        Assert.Equal(mode is 4 or 5 ? (byte)160 : (byte)128, image[12, 5].A);
        if (mode == 0) { Assert.Equal(new Rgba32(20, 30, 40, 128), image[12, 5]); }
        if (mode == 1) { Assert.Equal(new Rgba32(200, 77, 11, 128), image[12, 5]); }
        using MemoryStream rawStream = new(bytes);
        using JxlRawImage raw = new JxlDecoder().DecodeRaw(Configuration.Default, rawStream, TestContext.Current.CancellationToken);
        Assert.Single(raw.Frames); Assert.Equal(5, raw.Frames[0].Planes.Count);
        Assert.Equal(JxlExtraChannelType.Depth, raw.Frames[0].Planes[3].ExtraChannelType);
        Assert.Equal(JxlExtraChannelType.Alpha, raw.Frames[0].Planes[4].ExtraChannelType);
    }

    [Theory]
    [InlineData(0, 32, 1)] [InlineData(1, 32, 7)] [InlineData(2, 32, 7)] [InlineData(3, 32, 7)]
    [InlineData(4, 32, 7)] [InlineData(5, 32, 7)] [InlineData(6, 32, 7)] [InlineData(7, 32, 7)]
    [InlineData(4, 263, 10)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_patch_rendering_agrees_with_one_alpha_among_multiple_extra_channels(int mode, int width, int effort)
    {
        JxlExternalResources.RequireEnabled();
        await CompareWithNative(Encode((uint)mode, width, effort));
    }

    [Fact]
    public void frame_blending_uses_the_alpha_channels_own_reference_and_updates_its_output()
    {
        using MemoryStream input = new(EncodeFrameBlending()); using Image<Rgba32> image = Image.Load<Rgba32>(input);
        Assert.Equal(new Rgba32(164, 68, 17, 160), image[0, 0]);
    }

    [Fact]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_frame_blending_agrees_when_color_and_alpha_use_different_references()
    {
        JxlExternalResources.RequireEnabled();
        await CompareWithNative(EncodeFrameBlending());
    }

    private static async Task CompareWithNative(byte[] bytes)
    {
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        using MemoryStream stream = new(bytes); using Image<Rgba32> managed = Image.Load<Rgba32>(stream);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-patches-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, bytes, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=8", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba32> native = Image.Load<Rgba32>(output);
            Assert.Equal((managed.Width, managed.Height), (native.Width, native.Height));
            for (int y = 0; y < managed.Height; y++)
            {
                for (int x = 0; x < managed.Width; x++)
                {
                    Rgba32 a = managed[x, y], b = native[x, y];
                    Assert.InRange(Math.Abs(a.R - b.R), 0, 1); Assert.InRange(Math.Abs(a.G - b.G), 0, 1);
                    Assert.InRange(Math.Abs(a.B - b.B), 0, 1); Assert.InRange(Math.Abs(a.A - b.A), 0, 1);
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] EncodeFrameBlending()
    {
        var allocator = Configuration.Default.MemoryAllocator;
        JxlImageHeader image = new() { Size = new(16, 16), XybEncoded = false, BitDepth = new(8, 0),
            ExtraChannels = [new() { Type = 1, BitDepth = new(8, 0) }, new() { Type = 0, BitDepth = new(8, 0) }] };
        using MemoryStream output = new(); JxlBitWriter writer = new(output); image.Write(writer);
        for (int index = 1; index <= 3; index++)
        {
            bool reference = index != 3;
            JxlFrameHeader frame = new() { Modular = true, Width = 16, Height = 16, FrameType = reference ? 2U : 0,
                IsLast = !reference, SaveAsReference = reference ? (uint)index : 0, SaveBeforeColorTransform = false,
                ExtraUpsampling = [1, 1], ExtraBlending = reference ? [default, default] : [new(0, 0, false, 0), new(1, 0, false, 2)],
                Blend = reference ? default : new(2, 1, true, 1), Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
            int[] values = index == 1 ? [20, 30, 40, 0, 20] : index == 2 ? [220, 210, 200, 0, 64] : [200, 77, 11, 255, 128];
            using JxlModularWorkspace workspace = new(allocator, 256); JxlModularPlane[] planes = new JxlModularPlane[5];
            for (int c = 0; c < 5; c++)
            { planes[c] = workspace.Allocate(16, 16); for (int y = 0; y < 16; y++) { planes[c].Row(y).Fill(values[c]); } }
            JxlModularEncoder.WriteFrame(writer, image, frame, planes, TestContext.Current.CancellationToken, true, 1, allocator);
        }
        return output.ToArray();
    }

    [Fact]
    public void invalid_patch_fields_fail_before_any_bits_are_written()
    {
        JxlImageHeader image = new() { ExtraChannels = [] };
        JxlFrameHeader frame = new() { Width = 32, Height = 32 };
        JxlPatches[] invalid = [new([new(4, 0, 0, 1, 1, [new(0, 0, [default])])]),
            new([new(0, 0, 0, 1, 1, [])]), new([new(0, 0, 0, 8, 8, [new(30, 30, [default])])]),
            new([new(0, 0, 0, 1, 1, [new(0, 0, [new(4, 0, false, 0)])])])];
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        foreach (JxlPatches patches in invalid)
        { Assert.Throws<ArgumentException>(() => patches.Write(writer, image, frame, 7, TestContext.Current.CancellationToken)); Assert.Equal(0, writer.Position); }
    }

    private static byte[] Encode(uint mode, int width, int effort)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        JxlImageHeader image = new() { Size = new((uint)width, 17), XybEncoded = false, BitDepth = new(8, 0),
            ExtraChannels = [new() { Type = 1, BitDepth = new(8, 0) }, new() { Type = 0, BitDepth = new(8, 0) }] };
        JxlFrameHeader reference = new() { Modular = true, FrameType = 2, Width = 8, Height = 8, HaveCrop = true, IsLast = false,
            SaveAsReference = 3, SaveBeforeColorTransform = true, ExtraUpsampling = [1, 1], ExtraBlending = [default, default],
            Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        JxlFrameHeader frame = new() { Modular = true, Width = (uint)width, Height = 17, ExtraUpsampling = [1, 1], ExtraBlending = [default, default],
            Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        JxlBlendingInfo[] blends = [new(mode, 1, true, 3), new(1, 0, false, 3), new(1, 0, false, 3)];
        JxlPatches patches = new([new(3, 2, 2, 4, 3, [new(12, 5, blends), new(1, 9, blends)])]);
        using MemoryStream output = new(); JxlBitWriter writer = new(output); image.Write(writer);
        Write(reference, [200, 77, 11, 255, 128], null); Write(frame, [20, 30, 40, 100, 64], patches);
        return output.ToArray();

        void Write(JxlFrameHeader header, int[] values, JxlPatches dictionary)
        {
            using JxlModularWorkspace workspace = new(allocator, (long)width * 17);
            JxlModularPlane[] planes = new JxlModularPlane[5];
            for (int c = 0; c < planes.Length; c++)
            {
                planes[c] = workspace.Allocate((int)header.Width, (int)header.Height);
                for (int y = 0; y < header.Height; y++) { planes[c].Row(y).Fill(values[c]); }
            }
            JxlModularEncoder.WriteFrame(writer, image, header, planes, TestContext.Current.CancellationToken, effort == 1, effort, allocator, dictionary);
        }
    }
}
