// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Vector butterflies adapted from jxl-oxide crates/jxl-render/src/vardct/
// x86_64/dct/mod.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Row-first evaluation preserves the managed scalar path's operation order.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal static partial class JxlDct
{
    private static void InverseVector(Span<float> values, int width, int height)
    {
        int length = Math.Max(width, height);
        Span<Vector128<float>> lanes = stackalloc Vector128<float>[length], scratch = stackalloc Vector128<float>[length];
        for (int y = 0; y < height; y += 4)
        {
            int offset = y * width;
            for (int x = 0; x < width; x++)
            { lanes[x] = Vector128.Create(values[offset + x], values[offset + width + x], values[offset + (2 * width) + x], values[offset + (3 * width) + x]); }
            InverseLanes(lanes[..width], scratch[..width]);
            for (int x = 0; x < width; x++)
            {
                Vector128<float> row = lanes[x];
                values[offset + x] = row.GetElement(0); values[offset + width + x] = row.GetElement(1);
                values[offset + (2 * width) + x] = row.GetElement(2); values[offset + (3 * width) + x] = row.GetElement(3);
            }
        }
        ref float data = ref MemoryMarshal.GetReference(values);
        for (int x = 0; x < width; x += 4)
        {
            for (int y = 0; y < height; y++) { lanes[y] = Vector128.LoadUnsafe(ref data, (nuint)((y * width) + x)); }
            InverseLanes(lanes[..height], scratch[..height]);
            for (int y = 0; y < height; y++) { lanes[y].StoreUnsafe(ref data, (nuint)((y * width) + x)); }
        }
    }

    // Encoder forward transform: four rows (then four columns) per vector, each
    // lane following the scalar Transform's exact operation order.
    private static void ForwardVector(Span<float> values, int width, int height)
    {
        int length = Math.Max(width, height);
        Span<Vector128<float>> lanes = stackalloc Vector128<float>[length], scratch = stackalloc Vector128<float>[length];
        for (int y = 0; y < height; y += 4)
        {
            int offset = y * width;
            for (int x = 0; x < width; x++)
            { lanes[x] = Vector128.Create(values[offset + x], values[offset + width + x], values[offset + (2 * width) + x], values[offset + (3 * width) + x]); }
            ForwardLanes(lanes[..width], scratch[..width]);
            for (int x = 0; x < width; x++)
            {
                Vector128<float> row = lanes[x];
                values[offset + x] = row.GetElement(0); values[offset + width + x] = row.GetElement(1);
                values[offset + (2 * width) + x] = row.GetElement(2); values[offset + (3 * width) + x] = row.GetElement(3);
            }
        }
        ref float data = ref MemoryMarshal.GetReference(values);
        for (int x = 0; x < width; x += 4)
        {
            for (int y = 0; y < height; y++) { lanes[y] = Vector128.LoadUnsafe(ref data, (nuint)((y * width) + x)); }
            ForwardLanes(lanes[..height], scratch[..height]);
            for (int y = 0; y < height; y++) { lanes[y].StoreUnsafe(ref data, (nuint)((y * width) + x)); }
        }
    }

    private static void ForwardLanes(Span<Vector128<float>> values, Span<Vector128<float>> scratch)
    {
        int n = values.Length;
        if (n == 4)
        {
            Vector128<float> a = values[0], b = values[1], c = values[2], d = values[3];
            Vector128<float> s0 = a + d, s1 = b + c, t0 = (a - d) * .5411961f, t1 = (b - c) * 1.306563f;
            Vector128<float> r0 = (t0 + t1) * .25f, r1 = (t0 - t1) * .25f;
            values[0] = (s0 + s1) * .25f; values[1] = (r0 * 1.4142135623730951f) + r1;
            values[2] = (s0 - s1) * .25f; values[3] = r1;
            return;
        }
        int half = n / 2;
        Span<Vector128<float>> even = scratch[..half], odd = scratch[half..];
        ReadOnlySpan<float> secants = Secants[BitOperations.Log2((uint)n)];
        for (int i = 0; i < half; i++)
        {
            even[i] = (values[i] + values[n - 1 - i]) * .5f;
            odd[i] = (values[i] - values[n - 1 - i]) * .5f * secants[i];
        }
        ForwardLanes(even, values[..half]); ForwardLanes(odd, values[half..]);
        odd[0] *= 1.4142135623730951f;
        for (int i = 0; i < half - 1; i++) { odd[i] += odd[i + 1]; }
        for (int i = 0; i < half; i++) { values[i * 2] = even[i]; values[(i * 2) + 1] = odd[i]; }
    }

    private static void InverseLanes(Span<Vector128<float>> values, Span<Vector128<float>> scratch)
    {
        int n = values.Length;
        if (n == 4) { Inverse4(ref values[0], ref values[1], ref values[2], ref values[3]); return; }
        if (n == 8) { Inverse8(ref values[0], ref values[1], ref values[2], ref values[3], ref values[4], ref values[5], ref values[6], ref values[7]); return; }
        int half = n / 2;
        Span<Vector128<float>> even = scratch[..half], odd = scratch[half..];
        for (int i = 0; i < half; i++) { even[i] = values[i * 2]; odd[i] = values[(i * 2) + 1]; }
        for (int i = half - 1; i > 0; i--) { odd[i] += odd[i - 1]; }
        odd[0] *= 1.4142135623730951f;
        InverseLanes(even, values[..half]); InverseLanes(odd, values[half..]);
        ReadOnlySpan<float> secants = Secants[BitOperations.Log2((uint)n)];
        for (int i = 0; i < half; i++)
        { Vector128<float> r = odd[i] * secants[i]; values[i] = even[i] + r; values[n - 1 - i] = even[i] - r; }
    }

    private static void Inverse8X8(Span<float> values)
    {
        Span<float> transposed = stackalloc float[64];
        ref float scratch = ref MemoryMarshal.GetReference(transposed), output = ref MemoryMarshal.GetReference(values);
        for (int y = 0; y < 8; y += 4)
        {
            Vector128<float> a = Gather4(values, y * 8), b = Gather4(values, (y * 8) + 1);
            Vector128<float> c = Gather4(values, (y * 8) + 2), d = Gather4(values, (y * 8) + 3);
            Vector128<float> e = Gather4(values, (y * 8) + 4), f = Gather4(values, (y * 8) + 5);
            Vector128<float> g = Gather4(values, (y * 8) + 6), h = Gather4(values, (y * 8) + 7);
            Inverse8(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h);
            a.StoreUnsafe(ref scratch, (nuint)y); b.StoreUnsafe(ref scratch, (nuint)(8 + y));
            c.StoreUnsafe(ref scratch, (nuint)(16 + y)); d.StoreUnsafe(ref scratch, (nuint)(24 + y));
            e.StoreUnsafe(ref scratch, (nuint)(32 + y)); f.StoreUnsafe(ref scratch, (nuint)(40 + y));
            g.StoreUnsafe(ref scratch, (nuint)(48 + y)); h.StoreUnsafe(ref scratch, (nuint)(56 + y));
        }
        for (int x = 0; x < 8; x += 4)
        {
            Vector128<float> a = Gather4(transposed, x * 8), b = Gather4(transposed, (x * 8) + 1);
            Vector128<float> c = Gather4(transposed, (x * 8) + 2), d = Gather4(transposed, (x * 8) + 3);
            Vector128<float> e = Gather4(transposed, (x * 8) + 4), f = Gather4(transposed, (x * 8) + 5);
            Vector128<float> g = Gather4(transposed, (x * 8) + 6), h = Gather4(transposed, (x * 8) + 7);
            Inverse8(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h);
            a.StoreUnsafe(ref output, (nuint)x); b.StoreUnsafe(ref output, (nuint)(8 + x));
            c.StoreUnsafe(ref output, (nuint)(16 + x)); d.StoreUnsafe(ref output, (nuint)(24 + x));
            e.StoreUnsafe(ref output, (nuint)(32 + x)); f.StoreUnsafe(ref output, (nuint)(40 + x));
            g.StoreUnsafe(ref output, (nuint)(48 + x)); h.StoreUnsafe(ref output, (nuint)(56 + x));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> Gather4(ReadOnlySpan<float> source, int index)
        => Vector128.Create(source[index], source[index + 8], source[index + 16], source[index + 24]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Inverse8(ref Vector128<float> a, ref Vector128<float> b, ref Vector128<float> c, ref Vector128<float> d,
        ref Vector128<float> e, ref Vector128<float> f, ref Vector128<float> g, ref Vector128<float> h)
    {
        Vector128<float> e0 = a, e1 = c, e2 = e, e3 = g;
        Vector128<float> o0 = b * 1.4142135623730951f, o1 = d + b, o2 = f + d, o3 = h + f;
        Inverse4(ref e0, ref e1, ref e2, ref e3); Inverse4(ref o0, ref o1, ref o2, ref o3);
        o0 *= Secants[3][0]; o1 *= Secants[3][1]; o2 *= Secants[3][2]; o3 *= Secants[3][3];
        a = e0 + o0; b = e1 + o1; c = e2 + o2; d = e3 + o3;
        e = e3 - o3; f = e2 - o2; g = e1 - o1; h = e0 - o0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Inverse4(ref Vector128<float> a, ref Vector128<float> b, ref Vector128<float> c, ref Vector128<float> d)
    {
        Vector128<float> t0 = b * 1.4142135623730951f, t1 = b + d;
        Vector128<float> r0 = (t0 + t1) * .5411961f, r1 = (t0 - t1) * 1.306563f;
        Vector128<float> sum = a + c, difference = a - c;
        a = sum + r0; b = difference + r1; c = difference - r1; d = sum - r0;
    }
}
