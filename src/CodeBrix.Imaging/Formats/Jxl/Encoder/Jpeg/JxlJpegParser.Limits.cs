// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Threading;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;

internal sealed partial class JxlJpegParser
{
    // Scan the marker envelopes before entering the unsupported-input fallback.
    // APP/COM data and entropy-stuffed FF bytes cannot be mistaken for scans.
    internal static void ValidateScanCount(ReadOnlySpan<byte> source, int maxScans, CancellationToken cancellationToken)
    {
        if (source.Length < 4 || source[0] != 255 || source[1] != 0xd8) { JxlThrowHelper.Invalid("missing JPEG start marker."); }
        int scans = 0, position = 2;
        while (position < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int next = source[position..].IndexOf((byte)255);
            if (next < 0 || next >= source.Length - position - 1) { break; }
            position += next;
            while (position < source.Length && source[position] == 255) { position++; }
            if (position == source.Length) { break; }
            byte marker = source[position++];
            if (marker == 0xd9) { return; }
            if (marker is 0 or 255 or 1 or 0xd8 or (>= 0xd0 and <= 0xd7)) { continue; }
            if (position > source.Length - 2) { JxlThrowHelper.Invalid("truncated JPEG marker length."); }
            int length = BinaryPrimitives.ReadUInt16BigEndian(source[position..]);
            if (length < 2 || length > source.Length - position) { JxlThrowHelper.Invalid("invalid JPEG marker length."); }
            if (marker == 0xda)
            {
                if (scans >= maxScans) { JxlThrowHelper.Invalid("JPEG input exceeds MaxScans."); }
                scans++;
            }
            position += length;
        }
        JxlThrowHelper.Invalid("missing JPEG end marker.");
    }
}
