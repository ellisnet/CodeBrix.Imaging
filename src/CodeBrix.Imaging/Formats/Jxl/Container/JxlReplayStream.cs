// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace CodeBrix.Imaging.Formats.Jxl.Container;

// Cache compressed input only when a forward-only source must support playback loops.
// A rewind may replay the cached prefix and then resume reading the original stream.
internal sealed class JxlReplayStream(Stream source, int limit) : Stream
{
    private readonly MemoryStream cache = new();
    private bool complete;
    private long position;
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => this.complete ? this.cache.Length : throw new NotSupportedException("Input length is not known yet.");
    public override long Position { get => this.position; set => this.Seek(value, SeekOrigin.Begin); }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty) { return 0; }
        if (this.position < this.cache.Length)
        {
            this.cache.Position = this.position;
            int replayed = this.cache.Read(buffer); this.position += replayed; return replayed;
        }
        if (this.complete) { return 0; }
        int remaining = limit - (int)this.cache.Length;
        if (remaining == 0)
        {
            if (source.ReadByte() < 0) { this.complete = true; return 0; }
            throw new InvalidImageContentException("JPEG XL compressed input exceeds the animation replay-cache limit.");
        }
        int read = source.Read(buffer[..Math.Min(buffer.Length, remaining)]);
        if (read == 0) { this.complete = true; return 0; }
        this.cache.Position = this.cache.Length; this.cache.Write(buffer[..read]); this.position += read;
        return read;
    }
    public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));
    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(this.position + offset),
            SeekOrigin.End => checked(this.Length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        if (target < 0 || target > this.cache.Length) { throw new ArgumentOutOfRangeException(nameof(offset)); }
        return this.position = target;
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { this.cache.Dispose(); } base.Dispose(disposing); }
}
