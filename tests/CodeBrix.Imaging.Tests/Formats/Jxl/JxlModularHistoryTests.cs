// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlModularHistoryTests
{
    [Theory]
    [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(true, 12)]
    public void many_channels_use_linear_history_storage_and_preserve_geometry_lookback(bool usePrevious, int geometries)
    {
        const int count = 16384;
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace workspace = new(allocator, count);
        JxlModularPlane storage = workspace.Allocate(count, 1); List<JxlModularPlane> channels = [];
        for (int i = 0; i < count; i++)
        {
            JxlModularPlane plane = storage.View(i, 0, 1, 1); plane.HorizontalShift = i % geometries / 3; plane.VerticalShift = i % geometries % 3;
            channels.Add(plane);
        }
        JxlToken[] tokens = new JxlToken[count]; JxlEntropyEncoder encoder = new(tokens, 1);
        using MemoryStream histogram = new(); JxlBitWriter header = new(histogram); encoder.WriteHeader(header); header.ZeroPadToByte(); histogram.Position = 0;
        using JxlEntropyDecoder distributions = JxlEntropyDecoder.Read(new(histogram, TestContext.Current.CancellationToken), 1, allocator);
        JxlMaTree tree = new() { Distributions = distributions, Nodes = usePrevious
            ? [new(17, 0, 1, 2, -1, 0, 0, 0), new(0, 0, 0, 0, 0, 0, -1, 1), new(0, 0, 0, 0, 0, 0, 1, 1)]
            : [new(0, 0, 0, 0, 0, 0, 1, 1)] };
        using MemoryStream encoded = new(); JxlBitWriter writer = new(encoded);
        writer.WriteBool(true); new JxlWeightedPredictorHeader().Write(writer); writer.WriteBits(2, 0);
        encoder.WriteTokens(writer, tokens, TestContext.Current.CancellationToken); writer.ZeroPadToByte(); encoded.Position = 0;
        JxlBitReader reader = new(encoded, TestContext.Current.CancellationToken);
        JxlModularImage image = JxlModularImage.Read(reader, channels, tree, workspace);
        long before = GC.GetAllocatedBytesForCurrentThread();
        image.DecodeAll(reader, 0, workspace);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 8 << 20, $"Channel lookback allocated {allocated} bytes.");
        for (int i = 0; i < count; i++) { Assert.Equal(usePrevious && (i / geometries) % 2 != 0 ? -1 : 1, storage.Get(i, 0)); }
    }
}
