// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-frame/src/data/noise.rs and
// crates/jxl-render/src/features/noise.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Rendering;

internal sealed class JxlNoise
{
    private readonly float[] lut = new float[9];

    internal static JxlNoise Read(JxlBitReader reader)
    {
        JxlNoise noise = new();
        for (int i = 0; i < 8; i++) { noise.lut[i] = reader.ReadBits(10) / 1024f; }
        noise.lut[8] = noise.lut[7];
        return noise;
    }

    internal void Apply(JxlFloatFrame frame, JxlFrameHeader header, int visible, int invisible,
        float correlationX, float correlationB, CancellationToken cancellationToken)
    {
        if (frame.ColorChannels != 3) { return; }
        int width = (int)header.Width, height = (int)header.Height, dimension = (int)header.GroupDimension;
        using JxlFloatFrame noise = new(frame.Allocator, 3, 3);
        for (int c = 0; c < 3; c++) { noise.Planes[c] = frame.Allocator.Allocate2D<float>(width, height); }
        Span<float> batch = stackalloc float[16];
        ulong seed = ((ulong)(uint)visible << 32) | (uint)invisible;
        for (int top = 0; top < height; top += dimension)
        {
            for (int left = 0; left < width; left += dimension)
            {
                RandomState random = new(seed, ((ulong)(uint)left << 32) | (uint)top);
                int w = Math.Min(dimension, width - left), h = Math.Min(dimension, height - top);
                for (int c = 0; c < 3; c++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Span<float> row = noise.Planes[c].DangerousGetRowSpan(top + y).Slice(left, w);
                        for (int x = 0; x < w; x += 16)
                        {
                            random.Next(batch);
                            batch[..Math.Min(16, w - x)].CopyTo(row[x..]);
                        }
                    }
                }
            }
        }
        Span<float> values = stackalloc float[3];
        for (int y = 0; y < frame.Planes[0].Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> xr = frame.Planes[0].DangerousGetRowSpan(y), yr = frame.Planes[1].DangerousGetRowSpan(y), br = frame.Planes[2].DangerousGetRowSpan(y);
            for (int x = 0; x < xr.Length; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    Buffer2D<float> plane = noise.Planes[c];
                    float sum = 0;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        Span<float> row = plane.DangerousGetRowSpan(JxlRestoration.Mirror(y + dy, height));
                        for (int dx = -2; dx <= 2; dx++) { sum += row[JxlRestoration.Mirror(x + dx, width)] * .16f; }
                    }
                    values[c] = sum - (plane.DangerousGetRowSpan(y)[x] * 4);
                }
                float sx = this.Strength((xr[x] + yr[x]) * 3), sy = this.Strength((yr[x] - xr[x]) * 3);
                float nx = .22f * sx * ((.0078125f * values[0]) + (.9921875f * values[2]));
                float ny = .22f * sy * ((.0078125f * values[1]) + (.9921875f * values[2]));
                xr[x] += (correlationX * (nx + ny)) + nx - ny;
                yr[x] += nx + ny; br[x] += correlationB * (nx + ny);
            }
        }
    }

    private float Strength(float value)
    {
        value = float.IsNaN(value) ? 0 : Math.Max(0, value);
        int index = (int)Math.Min(7, value);
        return ((this.lut[index + 1] - this.lut[index]) * (value - index)) + this.lut[index];
    }

    private sealed class RandomState
    {
        private readonly ulong[] s0 = new ulong[8], s1 = new ulong[8];
        internal RandomState(ulong seed0, ulong seed1)
        {
            this.s0[0] = Mix(unchecked(seed0 + 0x9E3779B97F4A7C15));
            this.s1[0] = Mix(unchecked(seed1 + 0x9E3779B97F4A7C15));
            for (int i = 1; i < 8; i++) { this.s0[i] = Mix(this.s0[i - 1]); this.s1[i] = Mix(this.s1[i - 1]); }
        }
        internal void Next(Span<float> batch)
        {
            for (int i = 0; i < 8; i++)
            {
                ulong a = this.s0[i], b = this.s1[i], sum = unchecked(a + b);
                this.s0[i] = b; a ^= a << 23; this.s1[i] = a ^ b ^ (a >> 18) ^ (b >> 5);
                batch[i * 2] = BitConverter.UInt32BitsToSingle(((uint)sum >> 9) | 0x3f800000);
                batch[(i * 2) + 1] = BitConverter.UInt32BitsToSingle(((uint)(sum >> 32) >> 9) | 0x3f800000);
            }
        }
        private static ulong Mix(ulong value)
        {
            value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9);
            value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EB);
            return value ^ (value >> 31);
        }
    }
}
