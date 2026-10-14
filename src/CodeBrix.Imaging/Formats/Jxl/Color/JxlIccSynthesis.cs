// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// ICC tag layout and transfer-function representation adapted from jxl-oxide
// crates/jxl-color/src/icc/synthesize.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using CodeBrix.Imaging.Formats.Jxl.Headers;

namespace CodeBrix.Imaging.Formats.Jxl.Color;

internal static class JxlIccSynthesis
{
    internal static byte[] Create(JxlColorEncoding encoding)
    {
        if (encoding.WantIcc || encoding.ColorSpace is not (0 or 1)) { throw new ArgumentException("An enumerated RGB or grayscale encoding is required.", nameof(encoding)); }
        JxlEnumeratedColorSpace color = new(encoding, 10000);
        List<(uint Signature, byte[] Data)> tags = [];
        tags.Add((0x64657363, Text($"CodeBrix JPEG XL {(encoding.ColorSpace == 1 ? "Gray" : "RGB")} WP{encoding.WhitePoint} P{encoding.Primaries} TF{(encoding.HasGamma ? "Gamma" + encoding.Gamma : encoding.TransferFunction)}")));
        tags.Add((0x63707274, Text("Copyright (c) 2026 Jeremy Ellis and contributors; Apache-2.0")));
        tags.Add((0x77747074, Xyz(encoding.ColorSpace == 1 ? color.WhiteXyz : new(.9642f, 1, .8249f))));
        byte[] curve = Curve(encoding, color);
        if (encoding.ColorSpace == 1) { tags.Add((0x6b545243, curve)); }
        else
        {
            byte[] adaptation = new byte[44]; U32(adaptation, 0, 0x73663332);
            for (int i = 0; i < 9; i++) { Fixed(adaptation, 8 + (4 * i), color.AdaptToD50.Values[i]); }
            tags.Add((0x63686164, adaptation));
            uint[] trc = [0x72545243, 0x67545243, 0x62545243], xyz = [0x7258595a, 0x6758595a, 0x6258595a];
            float[] matrix = color.ToXyzD50.Values;
            for (int c = 0; c < 3; c++)
            {
                tags.Add((trc[c], curve));
                tags.Add((xyz[c], Xyz(new(matrix[c], matrix[c + 3], matrix[c + 6]))));
            }
        }
        if (!encoding.HasGamma && encoding.TransferFunction is 16 or 18 && encoding.WhitePoint == 1 && encoding.Primaries is 1 or 9 or 11)
        {
            byte[] cicp = new byte[12]; U32(cicp, 0, 0x63696370);
            cicp[8] = (byte)(encoding.Primaries == 11 ? 12 : encoding.Primaries);
            cicp[9] = (byte)encoding.TransferFunction; cicp[11] = 1;
            tags.Add((0x63696370, cicp));
        }
        int size = 132 + (12 * tags.Count);
        Dictionary<byte[], int> offsets = new();
        foreach ((_, byte[] data) in tags)
        {
            if (offsets.ContainsKey(data)) { continue; }
            offsets.Add(data, size); size += (data.Length + 3) & ~3;
        }
        byte[] profile = new byte[size];
        U32(profile, 0, (uint)size); U32(profile, 4, 0x43427278); U32(profile, 8, 0x04400000);
        U32(profile, 12, 0x6d6e7472); U32(profile, 16, encoding.ColorSpace == 1 ? 0x47524159U : 0x52474220U);
        U32(profile, 20, 0x58595a20); U16(profile, 24, 2026); U16(profile, 26, 1); U16(profile, 28, 1);
        U32(profile, 36, 0x61637370); U32(profile, 64, encoding.RenderingIntent);
        Fixed(profile, 68, .9642f); Fixed(profile, 72, 1); Fixed(profile, 76, .8249f); U32(profile, 80, 0x43427278);
        U32(profile, 128, (uint)tags.Count);
        for (int i = 0; i < tags.Count; i++)
        {
            (uint signature, byte[] data) = tags[i]; int entry = 132 + (12 * i), offset = offsets[data];
            U32(profile, entry, signature); U32(profile, entry + 4, (uint)offset); U32(profile, entry + 8, (uint)data.Length);
            data.CopyTo(profile, offset);
        }
        return profile;
    }

    private static byte[] Curve(JxlColorEncoding encoding, JxlEnumeratedColorSpace color)
    {
        if (encoding.HasGamma)
        {
            if (encoding.Gamma == 0) { JxlThrowHelper.Invalid("zero color gamma cannot be represented in ICC."); }
            return Parametric(0, [1e7f / encoding.Gamma]);
        }
        switch (encoding.TransferFunction)
        {
            case 1: return Parametric(3, [1f / .45f, 1f / 1.099f, .099f / 1.099f, 1f / 4.5f, .081f]);
            case 13: return Parametric(3, [2.4f, 1f / 1.055f, .055f / 1.055f, 1f / 12.92f, .04045f]);
            case 17: return Parametric(0, [2.6f]);
            case 16:
            case 18:
                byte[] curve = new byte[12 + (4096 * 2)]; U32(curve, 0, 0x63757276); U32(curve, 8, 4096);
                for (int i = 0; i < 4096; i++)
                { U16(curve, 12 + (i * 2), (ushort)Math.Clamp(MathF.Round(color.DecodeTransfer(i / 4095f) * 65535), 0, 65535)); }
                return curve;
            default:
                byte[] identity = new byte[12]; U32(identity, 0, 0x63757276); return identity;
        }
    }
    private static byte[] Parametric(ushort type, float[] values)
    {
        byte[] bytes = new byte[12 + (4 * values.Length)]; U32(bytes, 0, 0x70617261); U16(bytes, 8, type);
        for (int i = 0; i < values.Length; i++) { Fixed(bytes, 12 + (i * 4), values[i]); }
        return bytes;
    }
    private static byte[] Xyz(Vector3 value)
    {
        byte[] bytes = new byte[20]; U32(bytes, 0, 0x58595a20); Fixed(bytes, 8, value.X); Fixed(bytes, 12, value.Y); Fixed(bytes, 16, value.Z); return bytes;
    }
    private static byte[] Text(string text)
    {
        byte[] encoded = Encoding.BigEndianUnicode.GetBytes(text), bytes = new byte[28 + encoded.Length];
        U32(bytes, 0, 0x6d6c7563); U32(bytes, 8, 1); U32(bytes, 12, 12); U32(bytes, 16, 0x656e5553);
        U32(bytes, 20, (uint)encoded.Length); U32(bytes, 24, 28); encoded.CopyTo(bytes, 28); return bytes;
    }
    private static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
    private static void U16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
    private static void Fixed(byte[] bytes, int offset, float value)
    {
        double fixedValue = Math.Round(value * 65536.0);
        if (!double.IsFinite(fixedValue) || fixedValue < int.MinValue || fixedValue > int.MaxValue) { JxlThrowHelper.Invalid("color coefficient is outside the ICC fixed-point range."); }
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), (int)fixedValue);
    }
}
