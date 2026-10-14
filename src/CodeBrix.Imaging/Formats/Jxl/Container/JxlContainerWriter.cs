// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Inverse box grammar of the attributed JxlCodestreamStream reader.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace CodeBrix.Imaging.Formats.Jxl.Container;

internal static class JxlContainerWriter
{
    internal static void Start(Stream output, int level, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (level is not (5 or 10)) { throw new ArgumentOutOfRangeException(nameof(level)); }
        output.Write(JxlConstants.ContainerSignature);
        WriteBox(output, "ftyp"u8, "jxl \0\0\0\0jxl "u8, cancellationToken);
        if (level == 10) { WriteBox(output, "jxll"u8, [10], cancellationToken); }
    }

    internal static void WriteBox(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload, CancellationToken cancellationToken)
    {
        WriteBoxHeader(output, type, (ulong)payload.Length, false, cancellationToken);
        while (!payload.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested(); int take = Math.Min(payload.Length, 65536);
            output.Write(payload[..take]); payload = payload[take..];
        }
    }

    internal static void WriteBox(Stream output, ReadOnlySpan<byte> type, Stream input, ulong payloadLength,
        bool extended, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) { throw new ArgumentException("Box payload must be readable.", nameof(input)); }
        WriteBoxHeader(output, type, payloadLength, extended, cancellationToken);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            while (payloadLength != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = input.Read(buffer.AsSpan(0, (int)Math.Min(65536UL, payloadLength)));
                if (count == 0) { throw new EndOfStreamException("JPEG XL box payload ended before its declared length."); }
                output.Write(buffer.AsSpan(0, count)); payloadLength -= (uint)count;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal static void WriteCodestreamPart(Stream output, uint index, bool last, ReadOnlySpan<byte> payload,
        CancellationToken cancellationToken)
    {
        if (index >= 1U << 31) { throw new ArgumentOutOfRangeException(nameof(index)); }
        WriteBoxHeader(output, "jxlp"u8, (ulong)payload.Length + 4, false, cancellationToken);
        Span<byte> sequence = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sequence, index | (last ? 1U << 31 : 0)); output.Write(sequence);
        while (!payload.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested(); int count = Math.Min(65536, payload.Length);
            output.Write(payload[..count]); payload = payload[count..];
        }
    }

    internal static void WriteBoxHeader(Stream output, ReadOnlySpan<byte> type, ulong payloadLength, bool extended,
        CancellationToken cancellationToken)
    {
        if (type.Length != 4) { throw new ArgumentException("A JPEG XL box type has four bytes.", nameof(type)); }
        if (payloadLength > ulong.MaxValue - 16) { throw new ArgumentOutOfRangeException(nameof(payloadLength)); }
        extended |= payloadLength > uint.MaxValue - 8UL;
        Span<byte> header = stackalloc byte[16]; type.CopyTo(header[4..8]);
        BinaryPrimitives.WriteUInt32BigEndian(header, extended ? 1 : (uint)payloadLength + 8);
        if (extended) { BinaryPrimitives.WriteUInt64BigEndian(header[8..], payloadLength + 16); }
        cancellationToken.ThrowIfCancellationRequested(); output.Write(header[..(extended ? 16 : 8)]);
    }

    internal static void WriteMetadata(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload,
        bool compress, int effort, CancellationToken cancellationToken)
    {
        if (type.Length != 4 || !(type.SequenceEqual("Exif"u8) || type.SequenceEqual("xml "u8) || type.SequenceEqual("jumb"u8)))
        { throw new ArgumentException("Unsupported JPEG XL metadata box type.", nameof(type)); }
        if (effort is < 0 or > 11) { throw new ArgumentOutOfRangeException(nameof(effort)); }
        cancellationToken.ThrowIfCancellationRequested();
        if (compress && payload.Length > 16)
        {
            // The destination is deliberately smaller than the original: a
            // failed compression attempt simply keeps the original payload.
            byte[] buffer = new byte[payload.Length - 1]; type.CopyTo(buffer);
            if (BrotliEncoder.TryCompress(payload, buffer.AsSpan(4), out int count, effort, 22))
            { WriteBox(output, "brob"u8, buffer.AsSpan(0, count + 4), cancellationToken); return; }
        }
        WriteBox(output, type, payload, cancellationToken);
    }

    internal static void BeginFinalCodestream(Stream output, CancellationToken cancellationToken)
    {
        // A final unbounded jxlc box permits forward-only output without a
        // second full-image buffer. Metadata must precede it.
        cancellationToken.ThrowIfCancellationRequested(); output.Write("\0\0\0\0jxlc"u8);
    }
}
