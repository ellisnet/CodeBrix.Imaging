// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Inverse of the jxl-oxide ICC command grammar in crates/jxl-color/src/icc/decode.rs,
// 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Encoded-size and command-expansion limits checked against libjxl icc_codec.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

namespace CodeBrix.Imaging.Formats.Jxl.Color;

internal static partial class JxlIccCodec
{
    internal static void Write(JxlBitWriter writer, ReadOnlySpan<byte> profile, int effort, CancellationToken cancellationToken)
        => WriteEncoded(writer, Encode(profile, cancellationToken), effort, cancellationToken);

    internal static void WriteEncoded(JxlBitWriter writer, byte[] encoded, int effort, CancellationToken cancellationToken)
    {
        writer.WriteU64((ulong)encoded.Length);
        if (encoded.Length >= 65536)
        {
            JxlEntropyEncoder.WriteBytePrefixStream(writer, encoded, 41, cancellationToken);
            return;
        }
        JxlToken[] tokens = new JxlToken[encoded.Length];
        byte previous = 0, second = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            if ((i & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            tokens[i] = new(Context(i, previous, second), encoded[i]); second = previous; previous = encoded[i];
        }
        JxlEntropyEncoder.WriteCompressed(writer, tokens, 41, 0, false, effort, cancellationToken);
    }

    internal static byte[] Encode(ReadOnlySpan<byte> profile, CancellationToken cancellationToken = default)
    {
        if (profile.Length > 1 << 28) { throw new ArgumentOutOfRangeException(nameof(profile)); }
        using MemoryStream commands = new(), data = new();
        Span<byte> residual = stackalloc byte[Math.Min(128, profile.Length)];
        for (int i = 0; i < residual.Length; i++)
        { residual[i] = unchecked((byte)(profile[i] - PredictHeader(i, (uint)profile.Length, residual))); }
        data.Write(residual);
        if (profile.Length > 128)
        {
            int position = 128;
            uint tags = profile.Length >= 132 ? BinaryPrimitives.ReadUInt32BigEndian(profile[128..]) : uint.MaxValue;
            bool validTags = tags <= (profile.Length - 132L) / 12;
            for (int i = 0; validTags && i < tags; i++)
            {
                if ((i & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                ReadOnlySpan<byte> tag = profile.Slice(132 + (12 * i), 12);
                uint start = BinaryPrimitives.ReadUInt32BigEndian(tag[4..]), size = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
                validTags = start <= profile.Length && size <= profile.Length - start;
            }
            if (!validTags) { WriteVarint(commands, 0); }
            else
            {
                WriteVarint(commands, tags + 1);
                uint previousStart = 128 + (12 * tags), previousSize = 0;
                for (int i = 0; i < tags; i++)
                {
                    if ((i & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                    ReadOnlySpan<byte> tag = profile.Slice(132 + (12 * i), 12);
                    uint start = BinaryPrimitives.ReadUInt32BigEndian(tag[4..]), size = BinaryPrimitives.ReadUInt32BigEndian(tag[8..]);
                    int code = 1;
                    for (int j = 4; j <= 20; j++) { if (tag[..4].SequenceEqual(CommonTag(j))) { code = j; break; } }
                    uint predictedSize = code is 5 or 6 or 7 or 8 or 9 or 10 or 20 ? 20U : previousSize;
                    bool explicitStart = (ulong)previousStart + previousSize != start, explicitSize = predictedSize != size;
                    commands.WriteByte((byte)(code | (explicitStart ? 64 : 0) | (explicitSize ? 128 : 0)));
                    if (code == 1) { data.Write(tag[..4]); }
                    if (explicitStart) { WriteVarint(commands, start); }
                    if (explicitSize) { WriteVarint(commands, size); }
                    previousStart = start; previousSize = size;
                }
                commands.WriteByte(0); position = 132 + ((int)tags * 12);
            }
            int literalBegin = position, iterations = 0;
            while (position < profile.Length)
            {
                if ((iterations++ & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                int type = DataType(profile[position..]);
                if (type == 0) { position++; continue; }
                FlushLiteral(commands, data, profile[literalBegin..position]);
                if (type == 16 && profile.Length - position >= 20)
                { commands.WriteByte(10); data.Write(profile.Slice(position + 8, 12)); position += 20; }
                else { commands.WriteByte((byte)type); position += 8; }
                literalBegin = position;
            }
            FlushLiteral(commands, data, profile[literalBegin..]);
        }
        using MemoryStream result = new();
        WriteVarint(result, (ulong)profile.Length); WriteVarint(result, (ulong)commands.Length);
        if (result.Length + commands.Length + data.Length > Math.Min((long)profile.Length + 65536, 1 << 28))
        {
            // Unusual tag tables can expand the command stream beyond the
            // decoder's permitted overhead. Literal commands preserve all bytes.
            result.SetLength(0); commands.SetLength(0);
            if (profile.Length > 128)
            {
                WriteVarint(commands, 0); commands.WriteByte(1); WriteVarint(commands, (ulong)profile.Length - 128);
            }
            WriteVarint(result, (ulong)profile.Length); WriteVarint(result, (ulong)commands.Length);
            if (result.Length + commands.Length + profile.Length > 1 << 28)
            { throw new ArgumentException("The encoded ICC profile exceeds the JPEG XL size limit.", nameof(profile)); }
            commands.WriteTo(result); result.Write(residual);
            if (profile.Length > 128) { result.Write(profile[128..]); }
            return result.ToArray();
        }
        commands.WriteTo(result); data.WriteTo(result);
        return result.ToArray();
    }

    private static int DataType(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != 0) { return 0; }
        for (int i = 16; i <= 23; i++) { if (data[..4].SequenceEqual(CommonData(i))) { return i; } }
        return 0;
    }

    private static void FlushLiteral(Stream commands, Stream data, ReadOnlySpan<byte> literal)
    {
        if (literal.IsEmpty) { return; }
        commands.WriteByte(1); WriteVarint(commands, (ulong)literal.Length); data.Write(literal);
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        do { byte part = (byte)(value & 127); value >>= 7; stream.WriteByte((byte)(part | (value == 0 ? 0 : 128))); } while (value != 0);
    }
}
