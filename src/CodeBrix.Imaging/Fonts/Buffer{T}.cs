// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Imaging.Fonts;

//was previously: namespace SixLabors.Fonts;

/// <summary>
///     A disposable buffer that is backed by an array pool.
/// </summary>
/// <typeparam name="T">The type of buffer element.</typeparam>
internal ref struct Buffer<T>
    where T : struct
{
    private int length;
    private readonly byte[] buffer;
    private bool isDisposed;

    public Buffer(int length)
    {
        Guard.MustBeGreaterThanOrEqualTo(length, 0, nameof(length));
        var itemSizeBytes = Unsafe.SizeOf<T>();
        var bufferSizeInBytes = length * itemSizeBytes;
        buffer = ArrayPool<byte>.Shared.Rent(bufferSizeInBytes);
        this.length = length;
        isDisposed = false;
    }

    public Span<T> GetSpan()
    {
        if (buffer is null) ThrowObjectDisposedException();
        return MemoryMarshal.Cast<byte, T>(buffer.AsSpan()).Slice(0, length);
    }

    public void Dispose()
    {
        if (isDisposed) return;

        ArrayPool<byte>.Shared.Return(buffer);
        length = 0;
        isDisposed = true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowObjectDisposedException()
    {
        throw new ObjectDisposedException("Buffer<T>");
    }
}
