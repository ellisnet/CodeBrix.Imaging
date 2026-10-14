// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Modular channel geometry adapted from jxl-oxide crates/jxl-modular/src/
// lib.rs and param.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d.
// Apache-2.0 option. Buffer ownership and views use CodeBrix.Memory.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Modular;

internal sealed class JxlModularPlane
{
    internal Buffer2D<int> Buffer;
    internal int Left, Top, Width, Height;
    internal int HorizontalShift, VerticalShift;
    internal int OriginalWidth, OriginalHeight;

    internal Span<int> Row(int y)
    {
        if ((uint)y >= this.Height) { throw new ArgumentOutOfRangeException(nameof(y)); }
        return this.Width == 0 ? Span<int>.Empty : this.Buffer.DangerousGetRowSpan(this.Top + y).Slice(this.Left, this.Width);
    }
    internal int Get(int x, int y) => this.Row(y)[x];
    internal void Set(int x, int y, int value) => this.Row(y)[x] = value;
    internal JxlModularPlane View(int left, int top, int width, int height)
    {
        if (left < 0 || top < 0 || width < 0 || height < 0 || left > this.Width - width || top > this.Height - height)
        { JxlThrowHelper.Invalid("Modular channel view exceeds its plane."); }
        return new()
        {
            Buffer = this.Buffer, Left = this.Left + left, Top = this.Top + top, Width = width, Height = height,
            HorizontalShift = this.HorizontalShift, VerticalShift = this.VerticalShift,
            OriginalWidth = this.OriginalWidth, OriginalHeight = this.OriginalHeight,
        };
    }
}

internal sealed class JxlModularWorkspace : IDisposable
{
    private readonly MemoryAllocator allocator;
    private readonly long maxPixels;
    private readonly SampleBudget budget;
    private readonly List<Buffer2D<int>> buffers = [];
    private long allocatedSamples;
    internal MemoryAllocator Allocator => this.allocator;

    internal JxlModularWorkspace(MemoryAllocator allocator, long maxPixels, long maxBytes = long.MaxValue)
        : this(allocator, maxPixels, new SampleBudget(Math.Min(maxBytes, allocator.MemoryGroupAllocationLimitBytes) / sizeof(int), false)) { }

    internal static JxlModularWorkspace ForEncoding(MemoryAllocator allocator, long maxPixels, long maxBytes = long.MaxValue)
        => new(allocator, maxPixels, new SampleBudget(Math.Min(maxBytes, allocator.MemoryGroupAllocationLimitBytes) / sizeof(int), true));

    private JxlModularWorkspace(MemoryAllocator allocator, long maxPixels, SampleBudget budget)
    { this.allocator = allocator; this.maxPixels = maxPixels; this.budget = budget; }

    internal JxlModularWorkspace CreateTemporary() => new(this.allocator, this.maxPixels, this.budget);

    internal JxlModularPlane Allocate(int width, int height, int hshift = 0, int vshift = 0)
    {
        // Independent LF groups keep their decoded planes in this shared workspace.
        lock (this.buffers) { return this.AllocateCore(width, height, hshift, vshift); }
    }

    private JxlModularPlane AllocateCore(int width, int height, int hshift, int vshift)
    {
        if (width < 0 || height < 0 || (long)width * height > this.maxPixels) { this.budget.Reject("Modular plane exceeds pixel limit."); }
        Buffer2D<int> buffer = null;
        if (width != 0 && height != 0)
        {
            long samples = (long)width * height;
            this.budget.Reserve(samples);
            try
            {
                buffer = this.allocator.Allocate2D<int>(width, height, AllocationOptions.Clean);
                this.buffers.Add(buffer);
                this.allocatedSamples += samples;
            }
            catch { buffer?.Dispose(); this.budget.Release(samples); throw; }
        }
        return new() { Buffer = buffer, Width = width, Height = height, OriginalWidth = width, OriginalHeight = height, HorizontalShift = hshift, VerticalShift = vshift };
    }

    public void Dispose()
    {
        lock (this.buffers)
        {
            foreach (Buffer2D<int> buffer in this.buffers) { buffer.Dispose(); }
            this.buffers.Clear();
            this.budget.Release(this.allocatedSamples);
            this.allocatedSamples = 0;
        }
    }

    // Parallel group workspaces share one reservation pool with their parent.
    // Disposing a group returns its reservation, not the parent's live planes.
    private sealed class SampleBudget(long limit, bool encoding)
    {
        private readonly object sync = new();
        private long used;

        internal void Reserve(long samples)
        {
            lock (this.sync)
            {
                if (samples > limit - this.used) { this.Reject("aggregate Modular buffers exceed the configured memory limit."); }
                this.used += samples;
            }
        }

        internal void Release(long samples) { lock (this.sync) { this.used -= samples; } }

        internal void Reject(string message)
        {
            if (encoding) { throw new InvalidMemoryOperationException("JPEG XL encoding: " + message); }
            JxlThrowHelper.Invalid(message);
        }
    }
}
