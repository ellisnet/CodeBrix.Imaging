// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

internal sealed class JxlTestStream(byte[] bytes, int chunkSize = 7) : Stream
{
    private readonly MemoryStream inner = new(bytes, false);
    internal bool IsDisposed { get; private set; }
    public override bool CanRead => !this.IsDisposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(Span<byte> destination) => this.inner.Read(destination[..Math.Min(chunkSize, destination.Length)]);
    public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        this.IsDisposed = true;
        if (disposing) { this.inner.Dispose(); }
        base.Dispose(disposing);
    }
}
