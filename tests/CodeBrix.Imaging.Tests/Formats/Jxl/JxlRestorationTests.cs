// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlRestorationTests
{
    [Theory]
    [InlineData(16, 3)] [InlineData(18, 3)] [InlineData(17, 2)] [InlineData(17, 4)]
    public void epf_rejects_different_color_plane_extents_before_simd_access(int width, int height)
    {
        MemoryAllocator allocator = Configuration.Default.MemoryAllocator;
        using JxlFloatFrame frame = new(allocator, 3, 3);
        for (int c = 0; c < 3; c++)
        { frame.Planes[c] = allocator.Allocate2D<float>(c == 1 ? width : 17, c == 1 ? height : 3, AllocationOptions.Clean); }
        JxlRestorationFilter filter = new() { GaborEnabled = false, EpfIterations = 1 };
        Assert.Contains("matching dimensions", Assert.Throws<InvalidImageContentException>(() =>
            JxlRestoration.Apply(frame, filter, TestContext.Current.CancellationToken)).Message);
    }
}
