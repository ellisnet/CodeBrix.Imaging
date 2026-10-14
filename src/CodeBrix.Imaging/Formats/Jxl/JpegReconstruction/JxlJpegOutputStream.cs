// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

// This adapter counts only this reconstruction's output and never owns the
// destination. Checking before each write also works for forward-only streams.
internal sealed class JxlJpegOutputStream(Stream destination, long limit) : Stream
{
    private long remaining = limit;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => destination.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this.Reserve(buffer.Length); destination.Write(buffer);
    }
    public override void Write(byte[] buffer, int offset, int count) => this.Write(buffer.AsSpan(offset, count));
    public override void WriteByte(byte value) { this.Reserve(1); destination.WriteByte(value); }
    private void Reserve(int count)
    {
        if (count > this.remaining) { JxlThrowHelper.Invalid("reconstructed JPEG exceeds MaxOutputBytes."); }
        this.remaining -= count;
    }
    public override void Flush() => destination.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
