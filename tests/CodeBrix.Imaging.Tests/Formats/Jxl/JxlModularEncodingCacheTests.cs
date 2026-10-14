// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlModularEncodingCacheTests
{
    [Theory]
    [InlineData(4, false)] [InlineData(7, false)] [InlineData(10, false)] [InlineData(11, true)]
    public void cached_bodies_match_uncached_bits_across_alignment_and_settings(int effort, bool prefix)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace workspace = new(allocator, 1024);
        JxlModularPlane[] planes = [workspace.Allocate(17, 13), workspace.Allocate(17, 13), workspace.Allocate(17, 13)];
        for (int c = 0; c < 3; c++) { for (int y = 0; y < 13; y++) { for (int x = 0; x < 17; x++) { planes[c].Set(x, y, (x * y + c * 19) % 37); } } }
        JxlModularEncodingCache cache = new(1 << 20);
        for (int alignment = 0; alignment < 8; alignment++)
        {
            byte[] expected = Encode(null, alignment), actual = Encode(cache, alignment);
            Assert.Equal(expected, actual);
        }
        Assert.Equal(7, cache.Hits);
        planes[0].Set(16, 12, 99);
        Assert.Equal(Encode(null, 5), Encode(cache, 5)); Assert.Equal(7, cache.Hits);
        planes[0].HorizontalShift = 1;
        Assert.Equal(Encode(null, 3), Encode(cache, 3)); Assert.Equal(7, cache.Hits);
        prefix = !prefix;
        Assert.Equal(Encode(null, 1), Encode(cache, 1)); Assert.Equal(7, cache.Hits);
        effort = effort == 4 ? 5 : 4;
        Assert.Equal(Encode(null, 2), Encode(cache, 2)); Assert.Equal(7, cache.Hits);
        JxlModularSearchOptions search = new() { NoWeighted = true, Predictor = 5 };
        Assert.Equal(Encode(null, 6, search), Encode(cache, 6, search)); Assert.Equal(7, cache.Hits);

        byte[] Encode(JxlModularEncodingCache reuse, int alignment, JxlModularSearchOptions search = null)
        {
            using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
            writer.WriteBits(alignment, 0);
            JxlModularEncoder.WriteStreamCore(writer, planes, 0, null, null, prefix, effort, allocator,
                TestContext.Current.CancellationToken, search: search, cache: reuse);
            writer.WriteBits(7, 91); writer.ZeroPadToByte(); return stream.ToArray();
        }
    }

    [Fact]
    public void hash_collisions_never_substitute_different_samples_or_geometry()
    {
        using JxlModularWorkspace workspace = new(Configuration.Default.MemoryAllocator, 1024);
        JxlModularPlane a = workspace.Allocate(3, 2), b = workspace.Allocate(3, 2);
        b.Set(2, 1, 7);
        var cancel = TestContext.Current.CancellationToken;
        JxlModularEncodingCache cache = new(8192);
        var key = JxlModularEncodingCache.GetKey([b], 7, false, 8, null, cancel);
        // Deliberately install a different snapshot under b's hash.
        cache.Add(key, [a], new(), [11], 8, cancel);
        Assert.Null(cache.Find(key, [b], cancel));
        Assert.NotNull(cache.Find(key, [a], cancel));
        a.HorizontalShift = 1; Assert.Null(cache.Find(key, [a], cancel));
        a.HorizontalShift = 0; a.Set(0, 0, 5); Assert.Null(cache.Find(key, [a], cancel));
    }

    [Fact]
    public void eviction_bounds_snapshots_and_preserves_recent_entries()
    {
        using JxlModularWorkspace workspace = new(Configuration.Default.MemoryAllocator, 1024);
        JxlModularPlane plane = workspace.Allocate(16, 16);
        var cancel = TestContext.Current.CancellationToken;
        JxlModularEncodingCache cache = new(4000);
        Assert.True(cache.CanStore([plane]));
        JxlModularEncodingCache.Key first = default;
        for (int i = 0; i < 100; i++)
        {
            plane.Set(0, 0, i);
            var key = JxlModularEncodingCache.GetKey([plane], 7, false, 8, null, cancel);
            if (i == 0) { first = key; }
            cache.Add(key, [plane], new(), [17], 8, cancel);
            Assert.InRange(cache.RetainedBytes, 0, 4000);
            Assert.NotNull(cache.Find(key, [plane], cancel));
        }
        plane.Set(0, 0, 0); Assert.Null(cache.Find(first, [plane], cancel));
        JxlModularEncodingCache disabled = new(0); Assert.False(disabled.CanStore([plane]));
    }
}
