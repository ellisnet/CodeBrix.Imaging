// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodeBrix.Imaging.Memory.Internals; //Was previously: namespace SixLabors.ImageSharp.Memory.Internals;

/// <summary>
/// Allocates and provides an <see cref="IMemoryOwner{T}"/> implementation giving
/// access to unmanaged buffers allocated by <see cref="Marshal.AllocHGlobal(int)"/>.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal sealed unsafe class UnmanagedBuffer<T> : MemoryManager<T>, IRefCounted
    where T : struct
{
    private readonly int lengthInElements;

    private readonly UnmanagedBufferLifetimeGuard lifetimeGuard;

    private int disposed;

    public UnmanagedBuffer(int lengthInElements, UnmanagedBufferLifetimeGuard lifetimeGuard)
    {
        DebugGuard.NotNull(lifetimeGuard, nameof(lifetimeGuard));

        this.lengthInElements = lengthInElements;
        this.lifetimeGuard = lifetimeGuard;
    }

    public void* Pointer => this.lifetimeGuard.Handle.Pointer;

    public override Span<T> GetSpan()
    {
        // Promoted from DebugGuard to runtime checks: accessing the unmanaged
        // pointer after disposal is a use-after-free and must fail in release
        // builds as well, not just in DEBUG.
        if (this.disposed == 1)
        {
            throw new ObjectDisposedException(this.GetType().Name);
        }

        if (this.lifetimeGuard.IsDisposed)
        {
            throw new ObjectDisposedException(this.lifetimeGuard.GetType().Name);
        }

        return new(this.Pointer, this.lengthInElements);
    }

    /// <inheritdoc />
    public override MemoryHandle Pin(int elementIndex = 0)
    {
        // Promoted from DebugGuard to runtime checks for the same reason as GetSpan().
        if (this.disposed == 1)
        {
            throw new ObjectDisposedException(this.GetType().Name);
        }

        if (this.lifetimeGuard.IsDisposed)
        {
            throw new ObjectDisposedException(this.lifetimeGuard.GetType().Name);
        }

        // Will be released in Unpin
        this.lifetimeGuard.AddRef();

        var pbData = Unsafe.Add<T>(this.Pointer, elementIndex);
        return new MemoryHandle(pbData, pinnable: this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        DebugGuard.IsTrue(disposing, nameof(disposing), "Unmanaged buffers should not have finalizer!");

        if (Interlocked.Exchange(ref this.disposed, 1) == 1)
        {
            // Already disposed
            return;
        }

        this.lifetimeGuard.Dispose();
    }

    /// <inheritdoc />
    public override void Unpin() => this.lifetimeGuard.ReleaseRef();

    public void AddRef() => this.lifetimeGuard.AddRef();

    public void ReleaseRef() => this.lifetimeGuard.ReleaseRef();

    public static UnmanagedBuffer<T> Allocate(int lengthInElements)
    {
        if (lengthInElements < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lengthInElements),
                lengthInElements,
                "The unmanaged buffer length must be non-negative.");
        }

        // Use a checked context so that lengthInElements * Unsafe.SizeOf<T>()
        // overflowing Int32 results in a deterministic OverflowException
        // instead of allocating an undersized native buffer (potential
        // out-of-bounds access in unsafe code).
        int lengthInBytes;
        try
        {
            lengthInBytes = checked(lengthInElements * Unsafe.SizeOf<T>());
        }
        catch (OverflowException ex)
        {
            throw new InvalidMemoryOperationException(
                $"Unmanaged buffer size overflows Int32 (length={lengthInElements}, sizeof(T)={Unsafe.SizeOf<T>()}).")
            {
                Source = ex.Source
            };
        }

        return new(lengthInElements, new UnmanagedBufferLifetimeGuard.FreeHandle(UnmanagedMemoryHandle.Allocate(lengthInBytes)));
    }
}