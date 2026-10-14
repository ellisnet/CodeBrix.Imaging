// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>An owned, packed animation canvas. Dispose after presentation to return its buffer to the allocator.</summary>
public sealed class JxlAnimationFrame : IDisposable
{
    private IDisposable owner;
    private readonly Memory<byte> pixels;

    internal JxlAnimationFrame(IDisposable owner, Memory<byte> pixels, int width, int height,
        JxlPixelFormat format, JxlFrameMetadata metadata, TimeSpan duration)
    {
        this.owner = owner; this.pixels = pixels; this.Width = width; this.Height = height;
        this.PixelFormat = format; this.Metadata = metadata; this.Duration = duration;
    }

    /// <summary>Gets the full oriented canvas width.</summary>
    public int Width { get; }
    /// <summary>Gets the full oriented canvas height.</summary>
    public int Height { get; }
    /// <summary>Gets the byte count per packed scanline.</summary>
    public int Stride => checked(this.Width * 4);
    /// <summary>Gets the packed byte order.</summary>
    public JxlPixelFormat PixelFormat { get; }
    /// <summary>Gets the frame's display duration, saturated to <see cref="TimeSpan.MaxValue"/> for unrepresentable times.</summary>
    public TimeSpan Duration { get; }
    /// <summary>Gets the presentation time relative to the beginning of the current loop.</summary>
    public TimeSpan Timestamp { get; internal set; }
    /// <summary>Gets the elapsed decoding and packing time, excluding time waiting for space in the playback queue.</summary>
    public TimeSpan DecodeTime { get; internal set; }
    /// <summary>Gets the zero-based playback iteration.</summary>
    public uint LoopIndex { get; internal set; }
    /// <summary>Gets the coded frame timing, name and layer metadata.</summary>
    public JxlFrameMetadata Metadata { get; }
    /// <summary>Gets tightly packed pixels, valid until this frame is disposed.</summary>
    public ReadOnlySpan<byte> Pixels => this.PixelMemory.Span;
    /// <summary>Gets tightly packed pixels for asynchronous presentation, valid until this frame is disposed.</summary>
    public ReadOnlyMemory<byte> PixelMemory
    {
        get { ObjectDisposedException.ThrowIf(this.owner == null, this); return this.pixels; }
    }

    /// <inheritdoc/>
    public void Dispose() => Interlocked.Exchange(ref this.owner, null)?.Dispose();
}
