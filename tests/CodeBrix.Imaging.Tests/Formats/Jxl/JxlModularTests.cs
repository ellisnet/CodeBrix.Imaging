// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlModularTests
{
    [Theory]
    [InlineData("alpha_nonpremultiplied")]
    [InlineData("alpha_triangles")]
    [InlineData("delta_palette")]
    [InlineData("lossless_pfm")]
    [InlineData("lz77_flower")]
    [InlineData("grayscale_public_university")]
    public void decodes_modular_entropy_streams_through_validated_end_states(string name)
    {
        using JxlModularWorkspace workspace = new(Configuration.Default.MemoryAllocator, 100_000_000);
        JxlModularFrame frame = DecodeFirstFrame(name, workspace, out JxlImageHeader header);
        Assert.Equal(header.ColorChannels + header.ExtraChannels.Length, frame.Planes.Length);
        Assert.Equal((int)header.Size.Width, frame.Planes[0].Width);
        Assert.Equal((int)header.Size.Height, frame.Planes[0].Height);
    }

    [Theory]
    [InlineData("alpha_nonpremultiplied")]
    [InlineData("alpha_triangles")]
    [InlineData("delta_palette")]
    [InlineData("lossless_pfm")]
    [InlineData("lz77_flower")]
    [InlineData("grayscale_public_university")]
    [InlineData("blendmodes")]
    [InlineData("sunset_logo")]
    [InlineData("animation_newtons_cradle")]
    [InlineData("patches_lossless")]
    [InlineData("bicycles")]
    [Trait("Category", "JxlConformance")]
    public async Task modular_samples_match_external_float_reference(string name)
    {
        await JxlPixelReference.VerifyAsync(name);
    }

    [Fact]
    public void multiplication_without_extra_channels_matches_normalized_sample_product()
    { VerifyMultiplication(); }

    internal static void VerifyMultiplication()
    {
        string path = Path.Combine(JxlHeaderTests.ResourceRoot, "mul_no_extra_channels");
        using JxlTestStream input = new(File.ReadAllBytes(Path.Combine(path, "input.jxl")), 1);
        using Image<RgbaVector> actual = new JxlDecoder().Decode<RgbaVector>(Configuration.Default, input, TestContext.Current.CancellationToken);
        Assert.Equal(new Size(8, 8), actual.Size());
        float product = (128f / 255) * (128f / 255);
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                Assert.Equal(new RgbaVector(product, product, product, 1), actual[x, y]);
            }
        }
    }

    private static JxlModularFrame DecodeFirstFrame(string name, JxlModularWorkspace workspace, out JxlImageHeader header)
    {
        using FileStream stream = File.OpenRead(Path.Combine(JxlHeaderTests.ResourceRoot, name, "input.jxl"));
        using JxlCodestreamStream container = new(stream, 64 << 20, true, TestContext.Current.CancellationToken);
        JxlBitReader reader = new(container, TestContext.Current.CancellationToken);
        header = JxlImageHeader.Read(reader, 100_000_000, 64 << 20);
        if (header.Color.WantIcc) { JxlIccCodec.ReadEncoded(reader, 64 << 20, workspace.Allocator); }
        JxlFrameHeader frame = JxlFrameHeader.Read(reader, header, 100_000_000, 64 << 20);
        JxlFrameToc toc = JxlFrameToc.Read(reader, frame, workspace.Allocator);
        return JxlModularFrame.Read(reader, header, frame, toc, workspace, TestContext.Current.CancellationToken);
    }
}
