using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;
using System;
using System.Buffers;
using Xunit;

namespace CodeBrix.Imaging.Tests.MemoryAllocators;

/// <summary>
/// Baseline tests for the public <see cref="MemoryAllocator"/> API.
///
/// These tests exist primarily to detect regressions caused by hardening of
/// internal unmanaged memory code (e.g. <c>UnmanagedBuffer&lt;T&gt;</c>'s
/// allocation size and lifetime checks). They cover the full span of
/// allocator code paths used by consumers:
///   - small allocations (shared array pool path),
///   - mid-size allocations (single pool buffer path),
///   - large allocations (non-pool unmanaged buffer path),
/// and the basic invariants around length, span access, clearing, and
/// disposal that the library promises today.
/// </summary>
public class MemoryAllocatorTests
{
    private readonly ITestOutputHelper _output;

    public MemoryAllocatorTests(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public void Default_ReturnsNonNullAllocator()
    {
        var allocator = MemoryAllocator.Default;

        Assert.NotNull(allocator);
        // Default should return the same instance each time.
        Assert.Same(allocator, MemoryAllocator.Default);
    }

    [Fact]
    public void Create_WithoutOptions_ReturnsUsableAllocator()
    {
        var allocator = MemoryAllocator.Create();

        using var buffer = allocator.Allocate<byte>(64);
        Assert.NotNull(buffer);
        Assert.True(buffer.Memory.Length >= 64);
    }

    [Fact]
    public void Create_WithOptions_AppliesAllocationLimit()
    {
        var allocator = MemoryAllocator.Create(new MemoryAllocatorOptions
        {
            AllocationLimitMegabytes = 1,
            MaximumPoolSizeMegabytes = 1
        });

        // Sanity: small allocation still works.
        using (var ok = allocator.Allocate<byte>(1024))
        {
            Assert.True(ok.Memory.Length >= 1024);
        }

        // Asking for more than the configured limit must throw the documented exception type.
        Assert.Throws<InvalidMemoryOperationException>(() =>
            allocator.Allocate<byte>(8 * 1024 * 1024));
    }

    [Fact]
    public void Allocate_NegativeLength_Throws()
    {
        var allocator = MemoryAllocator.Create();

        Assert.Throws<InvalidMemoryOperationException>(() => allocator.Allocate<byte>(-1));
    }

    [Fact]
    public void Allocate_ZeroLength_ReturnsEmptyOrZeroLengthBuffer()
    {
        var allocator = MemoryAllocator.Create();

        using var buffer = allocator.Allocate<byte>(0);

        Assert.NotNull(buffer);
        // Different code paths may return 0-length or rounded-up backing memory,
        // but consumer-visible Span access must not throw.
        var span = buffer.Memory.Span;
        Assert.True(span.Length >= 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(1024)]            // shared-array-pool path
    [InlineData(64 * 1024)]       // shared-array-pool path
    [InlineData(2 * 1024 * 1024)] // single-pool-buffer path
    public void Allocate_Byte_ReturnsBufferOfRequestedLength(int length)
    {
        var allocator = MemoryAllocator.Create();

        using var buffer = allocator.Allocate<byte>(length);

        Assert.NotNull(buffer);
        Assert.True(
            buffer.Memory.Length >= length,
            $"Expected memory length >= {length}, got {buffer.Memory.Length}.");

        // We only require that the first 'length' elements are usable.
        var span = buffer.Memory.Span.Slice(0, length);
        Assert.Equal(length, span.Length);
    }

    [Fact]
    public void Allocate_WithCleanFlag_ReturnsZeroedBuffer()
    {
        var allocator = MemoryAllocator.Create();

        using var buffer = allocator.Allocate<byte>(4096, AllocationOptions.Clean);

        var span = buffer.Memory.Span.Slice(0, 4096);
        for (var i = 0; i < span.Length; i++)
        {
            Assert.Equal(0, span[i]);
        }
    }

    [Fact]
    public void Allocate_AllowsReadAndWriteThroughSpan()
    {
        var allocator = MemoryAllocator.Create();

        using var buffer = allocator.Allocate<int>(256);

        var span = buffer.Memory.Span;
        for (var i = 0; i < 256; i++)
        {
            span[i] = i * 3;
        }

        for (var i = 0; i < 256; i++)
        {
            Assert.Equal(i * 3, span[i]);
        }
    }

    [Fact]
    public void Allocate_OfPixelType_ReturnsCorrectlySizedBuffer()
    {
        // Mirrors how the rest of the library uses the allocator: a struct pixel type.
        var allocator = MemoryAllocator.Create();

        using var buffer = allocator.Allocate<Rgba32>(128);

        Assert.True(buffer.Memory.Length >= 128);

        var span = buffer.Memory.Span;
        span[0] = new Rgba32(1, 2, 3, 4);
        span[127] = new Rgba32(255, 254, 253, 252);

        Assert.Equal(new Rgba32(1, 2, 3, 4), span[0]);
        Assert.Equal(new Rgba32(255, 254, 253, 252), span[127]);
    }

    [Fact]
    public void Allocate_DisposeIsIdempotent()
    {
        var allocator = MemoryAllocator.Create();
        var buffer = allocator.Allocate<byte>(1024);

        buffer.Dispose();
        // A second dispose must not throw - this is part of the public IDisposable contract
        // and is exactly the sort of behavior the UnmanagedBuffer<T> hardening must preserve.
        var ex = Record.Exception(() => buffer.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Allocate_MultipleBuffersAreIndependent()
    {
        var allocator = MemoryAllocator.Create();

        using var a = allocator.Allocate<byte>(512, AllocationOptions.Clean);
        using var b = allocator.Allocate<byte>(512, AllocationOptions.Clean);

        a.Memory.Span[0] = 0xAB;
        b.Memory.Span[0] = 0xCD;

        Assert.Equal(0xAB, a.Memory.Span[0]);
        Assert.Equal(0xCD, b.Memory.Span[0]);
    }

    [Fact]
    public void Allocate_LargeBufferUsesNonPoolPath_AndIsUsable()
    {
        // 16 MB allocation goes well past the single-pool-buffer threshold,
        // forcing the non-pool unmanaged allocation path that is the focus
        // of the UnmanagedBuffer<T> hardening work.
        const int length = 16 * 1024 * 1024;

        var allocator = MemoryAllocator.Create();
        using var buffer = allocator.Allocate<byte>(length, AllocationOptions.Clean);

        Assert.True(buffer.Memory.Length >= length);

        var span = buffer.Memory.Span;

        // Touch the boundaries to ensure the unmanaged region is fully accessible.
        span[0] = 0x11;
        span[length - 1] = 0x22;

        Assert.Equal(0x11, span[0]);
        Assert.Equal(0x22, span[length - 1]);

        _output.WriteLine($"Allocated {length:N0} bytes via non-pool path; backing length = {buffer.Memory.Length:N0}.");
    }

    [Fact]
    public void Allocate_ReturnsIMemoryOwner()
    {
        // Consumers depend on the IMemoryOwner<T> contract.
        var allocator = MemoryAllocator.Create();

        IMemoryOwner<byte> owner = allocator.Allocate<byte>(32);
        try
        {
            Assert.NotNull(owner);
            Assert.True(owner.Memory.Length >= 32);
        }
        finally
        {
            owner.Dispose();
        }
    }

    [Fact]
    public void ReleaseRetainedResources_DoesNotThrow()
    {
        var allocator = MemoryAllocator.Create();

        // Warm the pool with a few allocations so there is something to release.
        for (var i = 0; i < 3; i++)
        {
            using var _ = allocator.Allocate<byte>(2 * 1024 * 1024);
        }

        var ex = Record.Exception(() => allocator.ReleaseRetainedResources());
        Assert.Null(ex);

        // Should still be usable after release.
        using var buffer = allocator.Allocate<byte>(256);
        Assert.True(buffer.Memory.Length >= 256);
    }
}
