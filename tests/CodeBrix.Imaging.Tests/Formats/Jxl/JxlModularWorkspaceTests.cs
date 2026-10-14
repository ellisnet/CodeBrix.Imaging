// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlModularWorkspaceTests
{
    [Fact]
    public void parent_and_nested_temporaries_share_and_return_reservations()
    {
        using JxlModularWorkspace parent = new(new SimpleGcMemoryAllocator(), 100, 80);
        parent.Allocate(2, 5);
        using (JxlModularWorkspace first = parent.CreateTemporary())
        {
            first.Allocate(1, 5);
            using (JxlModularWorkspace second = first.CreateTemporary())
            {
                second.Allocate(1, 5);
                Assert.Throws<InvalidImageContentException>(() => parent.Allocate(1, 1));
                Assert.Throws<InvalidImageContentException>(() => first.Allocate(1, 1));
                Assert.Throws<InvalidImageContentException>(() => second.Allocate(1, 1));
            }
            first.Allocate(1, 5);
        }
        parent.Allocate(2, 5);
        Assert.Throws<InvalidImageContentException>(() => parent.Allocate(1, 1));
    }

    [Fact]
    public void parallel_groups_cannot_each_spend_the_full_parent_budget()
    {
        using JxlModularWorkspace parent = new(new SimpleGcMemoryAllocator(), 1000, 516);
        parent.Allocate(1, 1);
        JxlModularWorkspace[] groups = new JxlModularWorkspace[16];
        int accepted = 0, rejected = 0;
        try
        {
            Parallel.For(0, groups.Length, new ParallelOptions { CancellationToken = TestContext.Current.CancellationToken }, i =>
            {
                groups[i] = parent.CreateTemporary();
                try { groups[i].Allocate(8, 8); Interlocked.Increment(ref accepted); }
                catch (InvalidImageContentException) { Interlocked.Increment(ref rejected); }
            });
            Assert.Equal(2, accepted); Assert.Equal(14, rejected);
        }
        finally { foreach (JxlModularWorkspace group in groups) { group?.Dispose(); } }
        parent.Allocate(16, 8);
        Assert.Throws<InvalidImageContentException>(() => parent.Allocate(1, 1));
    }

    [Fact]
    public void failed_allocator_call_returns_its_reservation()
    {
        using JxlModularWorkspace workspace = new(new FailOnceAllocator(), 100, 400);
        Assert.Throws<InvalidMemoryOperationException>(() => workspace.Allocate(10, 10));
        workspace.Allocate(10, 10);
        Assert.Throws<InvalidImageContentException>(() => workspace.Allocate(1, 1));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void public_decoder_applies_modular_byte_limit(bool raw)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "integer31.jxl"));
        using MemoryStream input = new(bytes, false);
        JxlDecoder decoder = new() { MaxModularBytes = 7 };
        InvalidImageContentException error = Assert.Throws<InvalidImageContentException>(() =>
        {
            if (raw) { using JxlRawImage image = decoder.DecodeRaw(Configuration.Default, input, TestContext.Current.CancellationToken); }
            else { using Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration.Default, input, TestContext.Current.CancellationToken); }
        });
        Assert.Contains("Modular buffers", error.Message);
    }

    private sealed class FailOnceAllocator : MemoryAllocator
    {
        private bool first = true;
        private readonly SimpleGcMemoryAllocator inner = new();
        protected internal override int GetBufferCapacityInBytes() => 1 << 20;
        public override IMemoryOwner<T> Allocate<T>(int length, AllocationOptions options = AllocationOptions.None)
        {
            if (this.first) { this.first = false; throw new InvalidMemoryOperationException("Deliberate test failure."); }
            return this.inner.Allocate<T>(length, options);
        }
    }
}
