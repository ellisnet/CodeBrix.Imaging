// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlHfMetadataTests
{
    public static IEnumerable<object[]> Strategies()
    {
        for (int type = 0; type < 27; type++)
        { for (int sampling = 0; sampling < 4; sampling++) { yield return [type, sampling]; } }
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public void subsampling_permits_only_transforms_confined_to_one_eight_by_eight_block(int transform, int sampling)
    {
        JxlTransformType type = (JxlTransformType)transform; (int width, int height) = type.Blocks();
        int count = 1024 / (width * height);
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace input = new(allocator, 65536);
        JxlModularPlane[] planes = [input.Allocate(4, 4), input.Allocate(4, 4), input.Allocate(count, 2), input.Allocate(32, 32)];
        planes[2].Row(0).Fill(transform);
        using MemoryStream encoded = new(); JxlBitWriter writer = new(encoded);
        writer.WriteBits(10, (uint)count - 1);
        JxlModularEncoder.WriteStreamCore(writer, planes, 0, null, null, true, 1, allocator, TestContext.Current.CancellationToken);
        writer.ZeroPadToByte(); encoded.Position = 0;
        JxlFrameHeader frame = new() { Width = 256, Height = 256, JpegUpsampling = sampling switch { 0 => [0, 0, 0], 1 => [0, 1, 0], 2 => [0, 2, 0], _ => [1, 1, 1] } };
        using JxlModularWorkspace output = new(allocator, 65536);
        JxlBitReader reader = new(encoded, TestContext.Current.CancellationToken);
        if (sampling is 1 or 2 && (width > 1 || height > 1))
        {
            Assert.Contains("chroma subsampling", Assert.Throws<InvalidImageContentException>(() => Read()).Message);
        }
        else { Read(); }
        void Read()
        {
            using JxlHfMetadata metadata = JxlHfMetadata.Read(reader, frame, 0, 256, 256, 8, null, 65536, output);
            Assert.Equal(type, metadata.Blocks.DangerousGetRowSpan(0)[0].Transform);
        }
    }
}
