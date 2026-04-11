// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CodeBrix.Imaging.PixelFormats;
#if SUPPORTS_RUNTIME_INTRINSICS
using System.Runtime.Intrinsics.X86;
#endif

namespace CodeBrix.Imaging; //Was previously: namespace SixLabors.ImageSharp;

internal static partial class SimdUtils
{
    [MethodImpl(InliningOptions.ShortMethod)]
    internal static void PackFromRgbPlanes(
        Configuration configuration,
        ReadOnlySpan<byte> redChannel,
        ReadOnlySpan<byte> greenChannel,
        ReadOnlySpan<byte> blueChannel,
        Span<Rgb24> destination)
    {
        DebugGuard.IsTrue(greenChannel.Length == redChannel.Length, nameof(greenChannel), "Channels must be of same size!");
        DebugGuard.IsTrue(blueChannel.Length == redChannel.Length, nameof(blueChannel), "Channels must be of same size!");
        DebugGuard.IsTrue(destination.Length > redChannel.Length + 2, nameof(destination), "'destination' must contain a padding of 3 elements!");

#if SUPPORTS_RUNTIME_INTRINSICS
            if (Avx2.IsSupported)
            {
                HwIntrinsics.PackFromRgbPlanesAvx2Reduce(ref redChannel, ref greenChannel, ref blueChannel, ref destination);
            }
            else
#endif
        {
            PackFromRgbPlanesScalarBatchedReduce(ref redChannel, ref greenChannel, ref blueChannel, ref destination);
        }

        PackFromRgbPlanesRemainder(redChannel, greenChannel, blueChannel, destination);
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    internal static void PackFromRgbPlanes(
        Configuration configuration,
        ReadOnlySpan<byte> redChannel,
        ReadOnlySpan<byte> greenChannel,
        ReadOnlySpan<byte> blueChannel,
        Span<Rgba32> destination)
    {
        DebugGuard.IsTrue(greenChannel.Length == redChannel.Length, nameof(greenChannel), "Channels must be of same size!");
        DebugGuard.IsTrue(blueChannel.Length == redChannel.Length, nameof(blueChannel), "Channels must be of same size!");
        DebugGuard.IsTrue(destination.Length > redChannel.Length, nameof(destination), "'destination' span should not be shorter than the source channels!");

#if SUPPORTS_RUNTIME_INTRINSICS
            if (Avx2.IsSupported)
            {
                HwIntrinsics.PackFromRgbPlanesAvx2Reduce(ref redChannel, ref greenChannel, ref blueChannel, ref destination);
            }
            else
#endif
        {
            PackFromRgbPlanesScalarBatchedReduce(ref redChannel, ref greenChannel, ref blueChannel, ref destination);
        }

        PackFromRgbPlanesRemainder(redChannel, greenChannel, blueChannel, destination);
    }

    private static void PackFromRgbPlanesScalarBatchedReduce(
        ref ReadOnlySpan<byte> redChannel,
        ref ReadOnlySpan<byte> greenChannel,
        ref ReadOnlySpan<byte> blueChannel,
        ref Span<Rgb24> destination)
    {
        ref var r = ref Unsafe.As<byte, ByteTuple4>(ref MemoryMarshal.GetReference(redChannel));
        ref var g = ref Unsafe.As<byte, ByteTuple4>(ref MemoryMarshal.GetReference(greenChannel));
        ref var b = ref Unsafe.As<byte, ByteTuple4>(ref MemoryMarshal.GetReference(blueChannel));
        ref var rgb = ref MemoryMarshal.GetReference(destination);

        var count = redChannel.Length / 4;
        for (var i = 0; i < count; i++)
        {
            ref var d0 = ref Unsafe.Add(ref rgb, i * 4);
            ref var d1 = ref Unsafe.Add(ref d0, 1);
            ref var d2 = ref Unsafe.Add(ref d0, 2);
            ref var d3 = ref Unsafe.Add(ref d0, 3);

            ref var rr = ref Unsafe.Add(ref r, i);
            ref var gg = ref Unsafe.Add(ref g, i);
            ref var bb = ref Unsafe.Add(ref b, i);

            d0.R = rr.V0;
            d0.G = gg.V0;
            d0.B = bb.V0;

            d1.R = rr.V1;
            d1.G = gg.V1;
            d1.B = bb.V1;

            d2.R = rr.V2;
            d2.G = gg.V2;
            d2.B = bb.V2;

            d3.R = rr.V3;
            d3.G = gg.V3;
            d3.B = bb.V3;
        }

        var finished = count * 4;
        redChannel = redChannel.Slice(finished);
        greenChannel = greenChannel.Slice(finished);
        blueChannel = blueChannel.Slice(finished);
        destination = destination.Slice(finished);
    }

    private static void PackFromRgbPlanesScalarBatchedReduce(
        ref ReadOnlySpan<byte> redChannel,
        ref ReadOnlySpan<byte> greenChannel,
        ref ReadOnlySpan<byte> blueChannel,
        ref Span<Rgba32> destination)
    {
        ref var r = ref Unsafe.As<byte, ByteTuple4>(ref MemoryMarshal.GetReference(redChannel));
        ref var g = ref Unsafe.As<byte, ByteTuple4>(ref MemoryMarshal.GetReference(greenChannel));
        ref var b = ref Unsafe.As<byte, ByteTuple4>(ref MemoryMarshal.GetReference(blueChannel));
        ref var rgb = ref MemoryMarshal.GetReference(destination);

        var count = redChannel.Length / 4;
        destination.Fill(new Rgba32(0, 0, 0, 255));
        for (var i = 0; i < count; i++)
        {
            ref var d0 = ref Unsafe.Add(ref rgb, i * 4);
            ref var d1 = ref Unsafe.Add(ref d0, 1);
            ref var d2 = ref Unsafe.Add(ref d0, 2);
            ref var d3 = ref Unsafe.Add(ref d0, 3);

            ref var rr = ref Unsafe.Add(ref r, i);
            ref var gg = ref Unsafe.Add(ref g, i);
            ref var bb = ref Unsafe.Add(ref b, i);

            d0.R = rr.V0;
            d0.G = gg.V0;
            d0.B = bb.V0;

            d1.R = rr.V1;
            d1.G = gg.V1;
            d1.B = bb.V1;

            d2.R = rr.V2;
            d2.G = gg.V2;
            d2.B = bb.V2;

            d3.R = rr.V3;
            d3.G = gg.V3;
            d3.B = bb.V3;
        }

        var finished = count * 4;
        redChannel = redChannel.Slice(finished);
        greenChannel = greenChannel.Slice(finished);
        blueChannel = blueChannel.Slice(finished);
        destination = destination.Slice(finished);
    }

    private static void PackFromRgbPlanesRemainder(
        ReadOnlySpan<byte> redChannel,
        ReadOnlySpan<byte> greenChannel,
        ReadOnlySpan<byte> blueChannel,
        Span<Rgb24> destination)
    {
        ref var r = ref MemoryMarshal.GetReference(redChannel);
        ref var g = ref MemoryMarshal.GetReference(greenChannel);
        ref var b = ref MemoryMarshal.GetReference(blueChannel);
        ref var rgb = ref MemoryMarshal.GetReference(destination);

        for (var i = 0; i < destination.Length; i++)
        {
            ref var d = ref Unsafe.Add(ref rgb, i);
            d.R = Unsafe.Add(ref r, i);
            d.G = Unsafe.Add(ref g, i);
            d.B = Unsafe.Add(ref b, i);
        }
    }

    private static void PackFromRgbPlanesRemainder(
        ReadOnlySpan<byte> redChannel,
        ReadOnlySpan<byte> greenChannel,
        ReadOnlySpan<byte> blueChannel,
        Span<Rgba32> destination)
    {
        ref var r = ref MemoryMarshal.GetReference(redChannel);
        ref var g = ref MemoryMarshal.GetReference(greenChannel);
        ref var b = ref MemoryMarshal.GetReference(blueChannel);
        ref var rgba = ref MemoryMarshal.GetReference(destination);

        for (var i = 0; i < destination.Length; i++)
        {
            ref var d = ref Unsafe.Add(ref rgba, i);
            d.R = Unsafe.Add(ref r, i);
            d.G = Unsafe.Add(ref g, i);
            d.B = Unsafe.Add(ref b, i);
            d.A = 255;
        }
    }
}